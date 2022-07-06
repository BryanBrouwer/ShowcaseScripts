using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Physics;
using Unity.Physics.Systems;
using Reese.Nav;
using UnityEngine.AI;

//handles all movement done on unit basis
public class FormationDynamicMovementSystem : SystemBase
{
    // ECBs for adding and removing of components, and destruction of Entities
    private BeginSimulationEntityCommandBufferSystem beginECB;
    private EndSimulationEntityCommandBufferSystem endECB;

    BuildPhysicsWorld buildPhysicsWorld => World.GetExistingSystem<BuildPhysicsWorld>();

    //flocking optimization using cells
    public NativeMultiHashMap<int, FlockComponent> cellVsEntityPositions;
    //calculation for getting cell key for the position of the entity
    public static int GetUniqueKeyForPosition(float3 position, int cellSize)
    {
        return (int)((15 * math.floor(position.x / cellSize)) + (17 * math.floor(position.y / cellSize)) + (19 * math.floor(position.z / cellSize)));
    }

    protected override void OnCreate()
    {
        beginECB = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        endECB = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        cellVsEntityPositions = new NativeMultiHashMap<int, FlockComponent>(0, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        var ecb = endECB.CreateCommandBuffer().AsParallelWriter();

        //setting the cellVSEntity Hashmap for later use
        EntityQuery eq = GetEntityQuery(typeof(FlockComponent));
        cellVsEntityPositions.Clear();
        if (eq.CalculateEntityCount() > cellVsEntityPositions.Capacity)
        {
            cellVsEntityPositions.Capacity = eq.CalculateEntityCount();
        }

        NativeMultiHashMap<int, FlockComponent>.ParallelWriter cellVsEntityPositionsParallel = cellVsEntityPositions.AsParallelWriter();
        Entities
        .WithNone<UnitDeathComponent, DeathStateComponent>()
        .ForEach((ref FlockComponent bc, ref Translation trans) =>
        {
            FlockComponent bcValues = new FlockComponent();
            bc.currentPosition = trans.Value;
            bcValues = bc;
            bcValues.currentPosition = trans.Value;
            cellVsEntityPositionsParallel.Add(GetUniqueKeyForPosition(trans.Value, bc.flockManager.cellSize), bcValues);
        })
        .WithName("Flocking_AddUnitToCellArray")
        .ScheduleParallel();

        float deltaTime = Time.DeltaTime;
        NativeMultiHashMap<int, FlockComponent> cellVsEntityPositionsForJob = cellVsEntityPositions;


        var translations = GetComponentDataFromEntity<Translation>(true);
        var disableFlockingComponents = GetComponentDataFromEntity<DisableFlockingComponent>(true);
        var localToWorldComponents = GetComponentDataFromEntity<LocalToWorld>(true);
        var formationComponents = GetComponentDataFromEntity<FormationComponent>(true);
        var unitEngageComponents = GetComponentDataFromEntity<UnitEngageComponent>(true);
        var meleeFormationComponents = GetComponentDataFromEntity<MeleeFormationComponent>(true);

        //Setting the new wanted formation position in the world
        Entities
        .WithReadOnly(translations)
        .WithReadOnly(localToWorldComponents)
        .WithReadOnly(formationComponents)
        .WithNone<UnitDeathComponent>()
        .ForEach((Entity entity, ref FlockComponent bc, ref UnitComponent unitComponent, in Translation trans, in UnitFormationPosComponent unitFormationPosComponent) =>
        {
            unitComponent.formationPos = unitFormationPosComponent.formationPos;

            float3 formationLeaderPosition = translations[bc.formation].Value;

            //This part will calculate the wanted formation position, including formation rotating
            LocalToWorld formationLocalToWorld = localToWorldComponents[bc.formation];
            //make a new 4x4 with that rotation and then add it to the localtoworld one
            //or math has a function for it
            var rotationMatrix = math.float4x4(formationComponents[bc.formation].formationRotation, float3.zero);
            var matrix = math.mul(formationLocalToWorld.Value, rotationMatrix);

            float4 targetPosition4 = math.mul(matrix, new float4(unitFormationPosComponent.formationPos.x, trans.Value.y, unitFormationPosComponent.formationPos.y, 1));

            float3 formationPosition = new float3(targetPosition4.x, bc.yHeight, targetPosition4.z);
            /////////////////////////////////
            ///
            bc.targetFormationPosition = formationPosition;
        })
        .WithName("Flocking_FormationPositionUpdate")
        .ScheduleParallel();

        var unitDisableFlockComponents = GetComponentDataFromEntity<UnitDisableFlocking>(true);

        //check if flocking should run
        Entities
        .WithReadOnly(disableFlockingComponents)
        .WithReadOnly(unitEngageComponents)
        .WithReadOnly(meleeFormationComponents)
        .WithReadOnly(unitDisableFlockComponents)
        .WithNone<UnitDeathComponent>()
        .ForEach((Entity entity, int entityInQueryIndex, ref FlockComponent bc, in Translation trans) =>
        {
            //bool old = bc.isActive;

            if (bc.stopAnimation)
            {
                bc.stopAnimationTimer -= deltaTime;
                if (/*!bc.blockingUnit &&*/ bc.stopAnimationTimer <= 0.0f)
                {
                    bc.stopAnimation = false;
                    bc.stopAnimationTimer = 0.3f;
                }
            }

            bc.shouldFlock = !(bc.blockingUnit || bc.blockingBuilding);

            bool disableFlocking = !disableFlockingComponents.HasComponent(bc.formation);
            if (unitEngageComponents.HasComponent(entity) && meleeFormationComponents.HasComponent(bc.formation))
            {
                bc.isActive = disableFlocking && bc.shouldFlock;
            }
            else if (bc.inFormation)
            {
                bc.isActive = disableFlocking && bc.shouldFlock && math.distance(bc.targetFormationPosition, trans.Value) > bc.flockManager.acceptanceDistance;
            }
            else
            {
                bc.isActive = disableFlocking && bc.shouldFlock && math.distance(bc.targetPos, trans.Value) > bc.flockManager.acceptanceDistance * 0.8f;
            }

            if (!bc.isActive && !unitDisableFlockComponents.HasComponent(entity))
            {
                ecb.AddComponent<UnitDisableFlocking>(entityInQueryIndex, entity);
                bc.velocity = float3.zero;
            }
            else if (bc.isActive && unitDisableFlockComponents.HasComponent(entity))
            {
                ecb.RemoveComponent<UnitDisableFlocking>(entityInQueryIndex, entity);
            }           
        })
        .WithName("Flocking_ActiveCheck")
        .ScheduleParallel();


        //setting unit height to terrai       
        var physicsWorld = buildPhysicsWorld.PhysicsWorld;           
        Dependency = JobHandle.CombineDependencies(Dependency, buildPhysicsWorld.GetOutputDependency());
            
        //prepare the seed for random value
            
        uint seed = 1 + (uint)Time.ElapsedTime;
            
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed);

        Entities            
        .WithNone<UnitDeathComponent, UnitDisableFlocking>()
        .WithReadOnly(physicsWorld)
        .ForEach((Entity entity, int entityInQueryIndex, ref FlockComponent bc, in Translation translation, in LocalToWorld localToWorld) =>
        {
            bc.yTimer -= deltaTime;
            if (bc.yTimer <= 0f)
            {
                float3 hitPosition = float3.zero;
                if (NavUtil.GetPointOnSurfaceLayer(physicsWorld, localToWorld, translation.Value, out hitPosition))
                {
                    bc.yHeight = hitPosition.y;
                    bc.yTimer = random.NextFloat(0.3f, 1f);
                }
            }


        })
        .WithName("Flocking_Grounding")
        .ScheduleParallel();
        
        Entities
        .WithReadOnly(cellVsEntityPositionsForJob)
        .WithNone<UnitDeathComponent, UnitDisableFlocking>()
        .ForEach((Entity entity, ref FlockComponent bc, in Translation trans) =>
        {
            int key = GetUniqueKeyForPosition(trans.Value, bc.flockManager.cellSize);
            NativeMultiHashMapIterator<int> nmhKeyIterator;
            FlockComponent neighbour;
            int totalAlignment = 0;
            int totalSeperate = 0;
            float3 separation = float3.zero;
            float3 alignment = float3.zero;
            float3 cohesion = float3.zero;
            float angle;

            // get the first neighbour for the current flock agent
            if (cellVsEntityPositionsForJob.TryGetFirstValue(key, out neighbour, out nmhKeyIterator))
            {
                do
                {
                    if (!bc.entity.Equals(neighbour.entity)
                    && math.distance(trans.Value, neighbour.currentPosition) < bc.flockManager.PerceptionRadius && bc.formation == neighbour.formation)
                    {
                        angle = math.acos(math.dot(bc.velocity, (neighbour.currentPosition - trans.Value)) / (math.length(bc.velocity) * math.length(neighbour.currentPosition - trans.Value)));
                        if (math.abs(angle) <= bc.flockManager.fieldOfView)
                        {
                            //if more operations have been done than the maxPerceive value then break out of loop
                            if (totalAlignment >= bc.flockManager.maxPerceived)
                            {
                                break;
                            }
                            float3 distanceFromTo = trans.Value - neighbour.currentPosition;
                            separation += (distanceFromTo / math.distance(trans.Value, neighbour.currentPosition));
                            alignment += neighbour.velocity;
                            totalSeperate++;
                            totalAlignment++;
                        }
                    }
                } while (cellVsEntityPositionsForJob.TryGetNextValue(out neighbour, ref nmhKeyIterator));
                if (totalAlignment > 0)
                {
                    alignment = alignment / totalAlignment;
                    alignment = alignment - bc.velocity;
                    alignment = math.normalize(alignment) * bc.flockManager.AlignmentBias;
                }
                if (totalSeperate > 0)
                {
                    separation = separation / totalSeperate;
                    separation = separation - bc.velocity;
                    separation = math.normalize(separation) * bc.flockManager.SeparationBias;

                }
                //override, if not inFormation it will use a target position set outside of this script to move towards
                if (bc.inFormation)
                {
                    cohesion = (math.normalize(bc.targetFormationPosition - trans.Value) * math.clamp(math.distance(trans.Value, bc.targetFormationPosition), 0.00f, 10.0f) ) * bc.flockManager.CohesionBias;
                }
                else
                {
                    cohesion = (math.normalize(bc.targetPos - trans.Value) * math.clamp(math.distance(trans.Value, bc.targetPos), 0.00f, 1.0f)) * bc.flockManager.CohesionBias;
                }

                //Combining all the forces
                float3 forces = (cohesion + separation + alignment);
                bc.velocity = bc.velocity + forces;
                //bc.velocity = math.normalizesafe(bc.velocity) * bc.speed;


            }
        })
        .WithName("Flocking_FlockingCalculation")
        .ScheduleParallel();

        var unitComponents = GetComponentDataFromEntity<UnitComponent>(true);
        var formationInAttackRangeComponents = GetComponentDataFromEntity<FormationInAttackRangeComponent>(true);
        //var unitAttackComponents = GetComponentDataFromEntity<UnitAttackComponent>(true);
        var attackDistance = CloseCombatPositioningSystem._attackDistance;

        //unit perception
        Entities
        .WithReadOnly(cellVsEntityPositionsForJob)
        .WithReadOnly(unitComponents)
        .WithReadOnly(formationInAttackRangeComponents)
        //.WithReadOnly(unitAttackComponents)
        .WithReadOnly(formationComponents)
        .WithReadOnly(meleeFormationComponents)
        .WithNone<UnitDeathComponent, MeleeUnitAttackComponent>()
        .ForEach((Entity entity, int entityInQueryIndex, ref FlockComponent bc, in Translation trans, in UnitComponent unitComponent) =>
        {
            int key = GetUniqueKeyForPosition(trans.Value, bc.flockManager.cellSize);
            NativeMultiHashMapIterator<int> nmhKeyIterator;
            FlockComponent neighbour;

            float angle;
            FlockComponent closestNeighbour = new FlockComponent();
            float closestDistance = 100000;
            int allySeen = 0;
            float avoidanceAngle = 0;
            bool closestIsAlly = true;
            bool found = false;
            //if (true)
            { 
                //targetting
                if (formationInAttackRangeComponents.HasComponent(bc.formation) && meleeFormationComponents.HasComponent(bc.formation))
                {
                    //Debug.Log("targetting");
                    if (cellVsEntityPositionsForJob.TryGetFirstValue(key, out neighbour, out nmhKeyIterator))
                    {
                        do
                        {
                            if (!bc.entity.Equals(neighbour.entity))
                            {
                                var distanceBetween = math.distance(trans.Value, neighbour.currentPosition);

                                if (distanceBetween < attackDistance * 1.0 && unitComponent.faction != unitComponents[neighbour.entity].faction)
                                {
                                    closestDistance = distanceBetween;
                                    closestNeighbour = neighbour;
                                    //Debug.Log("attack");
                                    closestIsAlly = false;
                                    found = true;
                                    break;
                                }

                                //check if distance is higher than saved distance, if so then skip.
                                if (distanceBetween > closestDistance)
                                {
                                    continue;
                                }


                                //sees enemy unit 
                                if (distanceBetween < attackDistance)
                                {
                                    if (unitComponent.faction != unitComponents[neighbour.entity].faction /*&& !unitAttackComponents.HasComponent(neighbour.entity)*/)
                                    {
                                        //if (unitComponents[neighbour.entity].targetedAmount <= 2)
                                        {

                                            closestDistance = distanceBetween;
                                            closestNeighbour = neighbour;
                                            //Debug.Log("attack");
                                            closestIsAlly = false;
                                            found = true;
                                            if (distanceBetween < attackDistance * 0.5f)
                                            {
                                                break;
                                            }
                                            continue;
                                        }
                                    }
                                }

                                //if is in collision range (check against friendlies)
                                if (distanceBetween < bc.flockManager.collisionRange && meleeFormationComponents.HasComponent(neighbour.formation))
                                {
                                    //if velocity is 0 then use rotation, but might want to break instead
                                    var tempcheck = new float3(0, 0, 0);
                                    if (bc.velocity.Equals(tempcheck))
                                    {
                                        bc.velocity = tempcheck;
                                    }
                                    angle = math.acos(math.dot(bc.velocity, (neighbour.currentPosition - trans.Value)) / (math.length(bc.velocity) * math.length(neighbour.currentPosition - trans.Value)));
                                    if (math.abs(angle) <= bc.flockManager.fieldOfView)
                                    {
                                        //seen unit is an ally 
                                        if (unitComponent.faction == unitComponents[neighbour.entity].faction)
                                        {
                                            closestDistance = distanceBetween;
                                            closestNeighbour = neighbour;

                                            allySeen++;
                                            avoidanceAngle = angle;
                                            closestIsAlly = true;
                                            found = true;

                                            continue;
                                        }
                                    }
                                }
                            }
                        } while (cellVsEntityPositionsForJob.TryGetNextValue(out neighbour, ref nmhKeyIterator));
                    }
                    if (found)
                    {
                        if (closestIsAlly && allySeen >= bc.flockManager.maxToCollide && math.distance(trans.Value, bc.targetPos) <= bc.flockManager.collisionRange * 2.5)
                        {
                            bc.blockingUnit = true;
                            bc.stopAnimation = true;
                            bc.stopAnimationTimer = 0.3f;
                            bc.velocity = float3.zero;
                        }
                        else if (closestIsAlly)
                        {
                            //Debug.Log("Avoiding ally");
                            bc.blockingUnit = false;

                            //Avoid ally unit

                            //direction away from centroid
                            var directionAway = trans.Value - formationComponents[bc.formation].formationCentroid;
                            directionAway = math.normalize(directionAway);
                            var tempDistance = math.distance(trans.Value, formationComponents[bc.formation].formationCentroid);
                            math.clamp(tempDistance, 0.20f, 10f);
                            directionAway = directionAway / tempDistance;

                            directionAway = directionAway * 0.5f;

                            var angleCalc = trans.Value - closestNeighbour.currentPosition;
                            angleCalc = math.normalize(angleCalc);
                            angleCalc = angleCalc / closestDistance;

                            angleCalc = angleCalc * 1f;

                            angleCalc = angleCalc + directionAway;

                            bc.velocity = bc.velocity + angleCalc * bc.flockManager.collisionForce;
                            //Debug.DrawLine(trans.Value, trans.Value + angleCalc * 10);
                        }
                        else
                        {
                            //Start attacking enemy unit

                            //important debug text
                            //if (unitComponents[entity].faction == FactionType.Enemy)
                            //{
                            //    Debug.Log("enemy unit start attacking friendly unit");
                            //}
                            //else if (unitComponents[entity].faction == FactionType.Player)
                            //{
                            //    Debug.Log("friendly unit start attacking enemy unit");
                            //}
                            ecb.AddComponent<MeleeUnitAttackComponent>(entityInQueryIndex, entity);
                            ecb.SetComponent(entityInQueryIndex, entity, new TargetComponent
                            {
                                target = closestNeighbour.entity
                            });

                            UnitComponent targetUnitComponent = unitComponents[closestNeighbour.entity];
                            targetUnitComponent.targetedAmount++;
                            ecb.SetComponent(entityInQueryIndex, closestNeighbour.entity, targetUnitComponent);

                            bc.blockingUnit = true;
                            bc.velocity = new float3(0, 0, 0);
                        }
                    }
                    else
                    {
                        bc.blockingUnit = false;
                    }
                }
            }
        })
        .WithName("Flocking_UnitPerception")
        .ScheduleParallel();

        //unit outside of combat collision
        Entities
        .WithReadOnly(cellVsEntityPositionsForJob)
        .WithReadOnly(unitComponents)
        .WithReadOnly(formationInAttackRangeComponents)
        .WithReadOnly(formationComponents)
        .WithNone<UnitDeathComponent, MeleeUnitAttackComponent, UnitDisableFlocking>()
        .WithAll<MoveStateComponent>()
        .ForEach((Entity entity, int entityInQueryIndex, ref FlockComponent bc, ref Translation trans, in UnitComponent unitComponent) =>
        {
            if (formationInAttackRangeComponents.HasComponent(bc.formation) || !bc.inFormation)
            {
                return;
            }
            //Debug.Log("running");
            //timer for optimization
            bc.collisionTimer -= deltaTime;
            if (!(bc.collisionTimer <= 0.0f))
            {
                return;
            }
            bc.collisionTimer = random.NextFloat(0.1f, 0.3f); ;

            int key = GetUniqueKeyForPosition(trans.Value, bc.flockManager.cellSize);
            NativeMultiHashMapIterator<int> nmhKeyIterator;
            FlockComponent neighbour;

            float angle;
            FlockComponent closestNeighbour = new FlockComponent();
            float closestDistance = 100000;
            int allySeen = 0;
            float avoidanceAngle = 0;
            bool closestIsAlly = true;
            bool found = false;
            if (true)
            {
                if (cellVsEntityPositionsForJob.TryGetFirstValue(key, out neighbour, out nmhKeyIterator))
                {
                    do
                    {
                        if (!bc.entity.Equals(neighbour.entity) && neighbour.isActive)
                        {
                            var distanceBetween = math.distance(trans.Value, neighbour.currentPosition);
                            //check if distance is higher than saved distance, if so then skip.
                            if (distanceBetween > closestDistance)
                            {
                                continue;
                            }
                            //if is in collision range (check against friendlies)
                            if (distanceBetween < bc.flockManager.collisionRange)
                            {
                                //if velocity is 0 then use rotation, but might want to break instead
                                var tempcheck = new float3(0, 0, 0);
                                if (bc.velocity.Equals(tempcheck))
                                {
                                    bc.velocity = tempcheck;
                                }
                                angle = math.acos(math.dot(bc.velocity, (neighbour.currentPosition - trans.Value)) / (math.length(bc.velocity) * math.length(neighbour.currentPosition - trans.Value)));
                                if (math.abs(angle) <= bc.flockManager.fieldOfView * 0.5)
                                {
                                    //seen unit is an ally 
                                    if (unitComponent.faction == unitComponents[neighbour.entity].faction)
                                    {

                                        closestDistance = distanceBetween;
                                        closestNeighbour = neighbour;

                                        allySeen++;
                                        avoidanceAngle = angle;
                                        closestIsAlly = true;
                                        found = true;

                                        //if spotted 2 allies then stop
                                        if (allySeen >= 3)
                                        {
                                            //Debug.Log("2 allies spotted, stopping..");
                                            //bc.velocity = float3.zero;
                                            break;
                                        }
                                        continue;
                                    }
                                }
                            }
                        }
                    } while (cellVsEntityPositionsForJob.TryGetNextValue(out neighbour, ref nmhKeyIterator));
                }
                //Debug.DrawLine(trans.Value, trans.Value + bc.velocity * 5, Color.red);

                //var formationFloat3 = new float3(unitComponent.formationPos.x, 0, unitComponent.formationPos.y);
                //formationFloat3 = math.normalize(formationFloat3);
                //Debug.DrawLine(trans.Value, trans.Value + formationFloat3 * 10, Color.blue);
                if (found)
                {
                    if (closestIsAlly && allySeen >= 3)
                    { 
                        bc.velocity = float3.zero;
                    }
                    else if (closestIsAlly)
                    {
                        //Debug.Log("Avoiding ally");
                        //Avoid ally unit

                        //direction away from centroid
                        var directionAway = trans.Value - formationComponents[bc.formation].formationCentroid;
                        directionAway = directionAway * math.distance(trans.Value, formationComponents[bc.formation].formationCentroid);
                        directionAway = math.normalize(directionAway);
                        directionAway = directionAway * 0.5f;


                        //var angleCalc = new float3(math.cos(avoidanceAngle), 0, math.sin(avoidanceAngle));
                        var angleCalc = trans.Value - closestNeighbour.currentPosition;
                        angleCalc = angleCalc / closestDistance;
                        angleCalc = math.normalize(angleCalc);
                        angleCalc = angleCalc * 1f;

                        angleCalc = angleCalc + directionAway;

                        bc.velocity = bc.velocity + angleCalc * bc.flockManager.collisionForce;
                        //Debug.DrawLine(trans.Value, trans.Value + angleCalc * 10);
                    }
                }                
            }
        })
        .WithName("Flocking_NonCombatCollision")
        .ScheduleParallel();

        //check if flocking should run
        Entities
        .WithNone<UnitDeathComponent, UnitDisableFlocking>()
        .ForEach((ref FlockComponent bc, ref Translation trans) =>
        {
            // Set the translation
            //normalize the velocity
            bc.velocity = new float3(bc.velocity.x, 0, bc.velocity.z);
            bc.velocity = math.normalizesafe(bc.velocity) * bc.speed;

            //setting the new translation value
            trans.Value = trans.Value + (bc.velocity) * math.clamp(deltaTime, 0.000f, 0.5f);
            trans.Value = math.lerp(trans.Value, new float3(trans.Value.x, bc.yHeight, trans.Value.z), deltaTime * 5);
            bc.currentPosition = trans.Value;
            //
        })
        .WithName("Flocking_SetTranslation")
        .ScheduleParallel();

        endECB.AddJobHandleForProducer(Dependency);
    }

    protected override void OnDestroy()
    {
        cellVsEntityPositions.Dispose();
    }
}
