using System;
using Tayx.Graphy.Utils.NumString;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Utilities;

//public enum FormationFormType
//{
//    SquareFormation = 0,
//    CircleFormation // not implemented
//}
//This system reforms the formations on a certain rule set.
[CreateAssetMenu(fileName = "FormationAssignmentSystem", menuName = "ScriptableObjects/Systems/FormationAssignmentSystem", order = 1)]
public class FormationAssignmentSystemAuthoring : SystemAuthoringSO
{
    [HideInInspector] [SerializeField] private SerializableGuid id;

    public override SystemBaseSO GetSystem()
        => World.DefaultGameObjectInjectionWorld.GetExistingSystem<FormationAssignmentSystem>();

    protected override void OnInit()
    {
        FormationAssignmentSystem assignmentSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystem<FormationAssignmentSystem>();
    }

    private void OnValidate()
    {
        if (id.Value == string.Empty)
        {
            id = Guid.NewGuid();
        }
    }
}

public class FormationAssignmentSystem : SystemBaseSO
{
    // ECBs for adding and removing of components, and destruction of Entities
    private BeginSimulationEntityCommandBufferSystem beginECB;
    private EndSimulationEntityCommandBufferSystem endECB;

    protected override void OnCreate()
    {
        beginECB = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        endECB = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var endSimEcb = endECB.CreateCommandBuffer().AsParallelWriter();

        Entities
        .WithAll<FormationExitEngageStateComponent>()
        .WithNone<FormationGarrisoned>()
        .ForEach(
        (Entity entity, int entityInQueryIndex) =>
        {
            endSimEcb.AddComponent(entityInQueryIndex, entity, new FormationReformComponent());
        })
        .ScheduleParallel();

        Entities
        .WithNone<FormationDeathComponent, FormationEngageStateComponent, FormationGarrisoned>()
        .WithAll<FormationReformComponent>()
        .ForEach(
            (Entity entity, int entityInQueryIndex,
            ref DynamicBuffer<FormationUnitElement> formationBuffer,
            in FormationComponent formation,
            in Translation trans) =>
            {
                int loopIndexX = -formation.count.x / 2;
                int loopIndexY = -formation.count.y / 2;
                for (int i = 0; i < formationBuffer.Length; i++)
                {
                    float newX = loopIndexX * formation.unitOffset.x;
                    float newY = loopIndexY * formation.unitOffset.y;

                    endSimEcb.SetComponent(entityInQueryIndex, formationBuffer[i].unit, new UnitFormationPosComponent()
                    {
                        formationPos = new float2(newX, newY)
                    });
                    loopIndexX++;
                    if (loopIndexX >= formation.count.x / 2)
                    {
                        loopIndexY++;
                        loopIndexX = -formation.count.x / 2;
                    }
                }
                endSimEcb.RemoveComponent<FormationReformComponent>(entityInQueryIndex, entity);
            })
            .ScheduleParallel();

        endECB.AddJobHandleForProducer(Dependency);
    }
}