using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
public class Orgre : MonoBehaviour
{
    public enum BehaviorState { Idle, Hurt, Die, Patrol, Chase, Attack, Investigate, InvestigateLastKnown }
    [Header("Health Status")]
    public int health;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Image fillImage;    

    [Header("Detection Settings")]
    [SerializeField] private Transform eyeStart;
    [SerializeField] private float chaseRange = 10f;
    [SerializeField] private float detectionAngle = 160f;
    [SerializeField] private float attackRange = 1.5f;
    List<Transform> allTargets = new List<Transform>();
    List<Transform> armyUnits = new List<Transform>();
    
    [Header("Ear Sensitivity")]
    [SerializeField] private Transform earPosition;
    [SerializeField] private float earRadius = 5f;
    [SerializeField] private float earDetectionAngle = 180f;

    [Header("Hyper Awareness")]
    [SerializeField] private float hyperAwarenessRange = 15f;
    [SerializeField] private float investigationDuration = 5f;

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private List<Transform> waypoints;

    [Header("Death Settings")]
    [SerializeField] private float deathDuration = 5f;
    float deathTimer;
    bool isDead = false;

    OrgreActions ogreActions;
    BTNode behaviorTree;
    NavMeshAgent agent;
    Transform player;
    BehaviorState currentBehaviorState = BehaviorState.Idle;

    Vector3 lastKnownPlayerPosition;
    float investigationTimer;
    bool hyperAwarenessActive;
    public Transform currentTarget;
    int currentWaypointIndex = 0;
    bool isAttacking = false;
    bool isHurt = false;
    int currentAttackIndex = -1;
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        player = GameObject.FindGameObjectWithTag("Player").transform;
        ScanForTargets();
        if (player != null) allTargets.Add(player);
        ogreActions = GetComponent<OrgreActions>();
        if (waypoints.Count > 0)
        {
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }
        if (healthSlider != null)
        {
            healthSlider.maxValue = health;
            healthSlider.value = health;
            UpdateHealthBarColor();
        }
        deathTimer = deathDuration;
        BuildBehaviorTree();
    }
    void BuildBehaviorTree()
    {
        behaviorTree = new Selector(
            // Highest priority first
            new Sequence( // Death
                new Condition(IsDead),
                new ActionNode(Die)
            ),
            new Sequence( // Hurt
                new Condition(IsHurt),
                new ActionNode(HandleHurt)
            ),
            
            new Sequence( // Attack
                new Condition(IsPlayerInAttackRange),
                new Condition(() => !isAttacking),
                new ActionNode(Attack)
            
        ), new Sequence( // Continue attacking 
                new Condition(() => isAttacking),
                new ActionNode(ContinueAttack)
            ),
            // Hyper awareness systems
            new Sequence( // Hyper awareness chase
                new Condition(() => hyperAwarenessActive),
                new ActionNode(ChaseTarget)
            ),
            new Sequence( // Investigate last known
                new Condition(ShouldInvestigateLastKnown),
                new ActionNode(InvestigateLastKnownPosition)
            ),
            // detection Systems
            new Sequence( // chase
                new Condition(IsPlayerDetected),
                new ActionNode(ChaseTarget)
            ),
            new Sequence( // Ear detection
                new Condition(IsPlayerDetectedByEar),
                new ActionNode(InvestigateEarPosition)
            ),
            new Sequence( // Patrol
                new ActionNode(Patrol)
            )
        );
    }
    void Update()
    {
        if ((IsPlayerDetected() || IsPlayerDetectedByEar()) && !hyperAwarenessActive)
        {
            hyperAwarenessActive = true;
            if (currentTarget != null)
                lastKnownPlayerPosition = currentTarget.position;
        }
        if (isDead)
        {
            deathTimer -= Time.deltaTime;

            if (deathTimer <= 0)
            {
                ResetOgre();
                gameObject.SetActive(false);
            }
            return;
        }
        behaviorTree.Execute();
        HandleAnimation();
    }
    void UpdateHealthBarColor()
    {
        if (fillImage != null)
        {
            float healthPercent = (float)health / healthSlider.maxValue;
        }
    }
    #region Detection Logic
    void ScanForTargets()
    {
        armyUnits.Clear();
        GameObject[] armyObjects = GameObject.FindGameObjectsWithTag("Army");
        foreach (GameObject unit in armyObjects)
        {
            armyUnits.Add(unit.transform);
        }
        allTargets.Clear();
        allTargets.AddRange(armyUnits);
        if (player != null) allTargets.Add(player);
        allTargets.RemoveAll(item => item == null);
    }
    bool Patrol()
    {
        if (IsPlayerDetected()) return false;

        currentBehaviorState = BehaviorState.Patrol;
        agent.speed = walkSpeed;

        if (waypoints.Count == 0) return false;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }
        agent.isStopped = false;
        return true;
    }
    bool IsPlayerInHyperAwareness()
    {
        return Vector3.Distance(transform.position, player.position) <= hyperAwarenessRange;
    }

    bool ShouldInvestigateLastKnown()
    {
        return !IsPlayerInHyperAwareness() && hyperAwarenessActive && investigationTimer > 0;
    }

    bool InvestigateLastKnownPosition()
    {
        currentBehaviorState = BehaviorState.InvestigateLastKnown;
        agent.speed = runSpeed;
        agent.SetDestination(lastKnownPlayerPosition);

        // Always decrement timer regardless of position
        investigationTimer -= Time.deltaTime;

        Debug.DrawLine(transform.position, lastKnownPlayerPosition, Color.cyan);

        if (investigationTimer <= 0 || agent.remainingDistance <= agent.stoppingDistance)
        {
            hyperAwarenessActive = false;
            currentBehaviorState = BehaviorState.Idle;
            agent.ResetPath();
        }
        return true;
    }
    bool IsPlayerDetected()
    {
        if (eyeStart == null) return false;

        foreach (Transform target in allTargets)
        {
            if (target == null) continue;

            Vector3 toTarget = target.position - eyeStart.position;
            float distance = toTarget.magnitude;
            if (distance > chaseRange) continue;

            float angle = Vector3.Angle(eyeStart.forward, toTarget.normalized);
            if (angle <= detectionAngle / 2f)
            {
                currentTarget = target;
                return true;
            }
        }
        return false;
    }
    bool IsPlayerDetectedByEar()
    {
        if (earPosition == null) return false;

        // Check if current target is valid and detected by ear
        if (currentTarget != null)
        {
            Vector3 toTarget = currentTarget.position - earPosition.position;
            float distance = toTarget.magnitude;
            if (distance > earRadius) return false;

            float angle = Vector3.Angle(earPosition.forward, toTarget.normalized);
            if (angle <= earDetectionAngle / 2f)
            {
                return true;
            }
            else
            {
                currentTarget = null;
            }
        }
        List<Transform> potentialTargets = new List<Transform>();
        GameObject[] armyUnits = GameObject.FindGameObjectsWithTag("Army");
        foreach (GameObject unit in armyUnits)
        {
            potentialTargets.Add(unit.transform);
        }
        if (player != null) potentialTargets.Add(player);
        List<Transform> detectedTargets = new List<Transform>();
        foreach (Transform target in potentialTargets)
        {
            Vector3 toTarget = target.position - earPosition.position;
            float distance = toTarget.magnitude;
            if (distance > earRadius) continue;

            float angle = Vector3.Angle(earPosition.forward, toTarget.normalized);
            if (angle <= earDetectionAngle / 2f)
            {
                detectedTargets.Add(target);
            }
        }

        if (detectedTargets.Count > 0)
        {
            currentTarget = detectedTargets[Random.Range(0, detectedTargets.Count)];
            return true;
        }

        return false;
    }

    bool InvestigateEarPosition()
    {
        currentBehaviorState = BehaviorState.Investigate;
        agent.speed = walkSpeed;
        agent.SetDestination(currentTarget.position);
        agent.isStopped = false;
        return true;
    }
    #endregion

    #region Attack Logic
    bool Attack()
    {
        if (!isAttacking)
        {

            currentAttackIndex = Random.Range(0, 3);
            currentBehaviorState = BehaviorState.Attack;
            transform.LookAt(currentTarget);
            isAttacking = true;
            GetComponent<OrgreAnimiation>().TriggerAttack(currentAttackIndex);
            ogreActions.PerformAttack();
        }
        return true;
    }
    bool IsPlayerInAttackRange()
    {
        if (currentTarget == null) return false;
        return Vector3.Distance(transform.position, currentTarget.position) <= attackRange;
    }

    bool ChaseTarget()
    {
        if (currentTarget == null) return false;

        lastKnownPlayerPosition = currentTarget.position;

        if (hyperAwarenessActive && !IsPlayerInHyperAwareness())
        {
            if (investigationTimer <= 0)
            {
                investigationTimer = investigationDuration;
            }
            return false;
        }

        currentBehaviorState = BehaviorState.Chase;
        agent.speed = runSpeed;
        agent.isStopped = false;
        agent.SetDestination(currentTarget.position);
        return true;
    }
    bool ContinueAttack()
    {
        currentBehaviorState = BehaviorState.Attack;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        return true;
    }
    #endregion

    #region Hurt & Death Logic
    public void TakeDamage(int damage)
    {
        health -= damage;
        if (healthSlider != null)
        {
            healthSlider.value = health;
            UpdateHealthBarColor();
        }
        if (health > 0)
        {
            isHurt = true;
        }
    }
    bool IsDead() => health <= 0;
    bool IsHurt() => isHurt;
    bool Die()
    {
        if (!isDead)
        {
            isDead = true;
            deathTimer = deathDuration;
            currentBehaviorState = BehaviorState.Die;
            agent.enabled = false;
            if (healthSlider != null)
            {
                healthSlider.gameObject.SetActive(false);
            }
            GetComponent<Collider>().enabled = false;
        }
        return true;
    }
    bool HandleHurt() { currentBehaviorState = BehaviorState.Hurt; isHurt = false; return true; }
    void ResetOgre()
    {
        // Reset health and state
        health = 15;
        isDead = false;
        currentBehaviorState = BehaviorState.Idle;
        isHurt = false;
        isAttacking = false;
        hyperAwarenessActive = false;
        deathTimer = deathDuration;

        // Re-enable components
        agent.enabled = true;
        GetComponent<Collider>().enabled = true;

        // Reset health bar
        if (healthSlider != null)
        {
            healthSlider.gameObject.SetActive(true);
            healthSlider.value = health;
            UpdateHealthBarColor();
        }

        // Reset position to start position
        if (waypoints.Count > 0)
        {
            transform.position = waypoints[0].position;
            agent.Warp(waypoints[0].position);
        }
    }
    #endregion

    #region Gizmos Represtation
    void OnDrawGizmos()
    {
        if (eyeStart != null)
        {
            Gizmos.color = Color.yellow;
            float halfAngle = detectionAngle / 2f;
            Vector3 leftDirection = Quaternion.AngleAxis(-halfAngle, Vector3.up) * eyeStart.forward;
            Vector3 rightDirection = Quaternion.AngleAxis(halfAngle, Vector3.up) * eyeStart.forward;

            Gizmos.DrawRay(eyeStart.position, leftDirection * chaseRange);
            Gizmos.DrawRay(eyeStart.position, rightDirection * chaseRange);
            DrawConeGizmo(eyeStart.position, eyeStart.forward, detectionAngle, chaseRange);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        if (earPosition != null)
        {
            Gizmos.color = Color.cyan;
            DrawConeGizmo(earPosition.position, earPosition.forward, earDetectionAngle, earRadius);
        }
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, hyperAwarenessRange);

    }

    void DrawConeGizmo(Vector3 pos, Vector3 dir, float angle, float range)
    {
        int segments = 20;
        float step = angle / segments;
        Quaternion rot = Quaternion.AngleAxis(-angle / 2, Vector3.up);

        Vector3 prevPoint = pos + rot * dir * range;
        for (int i = 0; i <= segments; i++)
        {
            Quaternion currentRot = Quaternion.AngleAxis(step * i - angle / 2, Vector3.up);
            Vector3 currentPoint = pos + currentRot * dir * range;
            Gizmos.DrawLine(prevPoint, currentPoint);
            prevPoint = currentPoint;
        }
    }
    #endregion

    #region Animation Handling
    void HandleAnimation()
    {
        bool idle = false, walk = false, run = false, attack = false, hurt = false, die = false;
        int attackIndex = -1;
        switch (currentBehaviorState)
        {
            case BehaviorState.Die: die = true; break;
            case BehaviorState.Hurt: hurt = true; break;
            case BehaviorState.Attack: attackIndex = currentAttackIndex; break;
            case BehaviorState.Chase: run = true; break;
            case BehaviorState.Patrol: walk = true; break;
            case BehaviorState.Idle: idle = true; break;
            case BehaviorState.Investigate: walk = true; break;
            default:
                idle = agent.velocity.magnitude < 0.1f;
                walk = !idle && agent.speed == walkSpeed;
                run = !idle && agent.speed == runSpeed;
                break;
        }

        GetComponent<OrgreAnimiation>().SetAnimatorBools(idle, walk, run, attackIndex, hurt, die);
    }
    public void OnAttackAnimationComplete()
    {
        Debug.Log($"Completed attack {currentAttackIndex}");
        isAttacking = false;
        currentAttackIndex = -1;
        agent.isStopped = false;
        if (IsPlayerInAttackRange())
        {
            behaviorTree.Execute();
        }
        else
        {
            agent.SetDestination(hyperAwarenessActive && currentTarget != null ?
                currentTarget.position :
                waypoints[currentWaypointIndex].position);
        }
    }
    #endregion
}