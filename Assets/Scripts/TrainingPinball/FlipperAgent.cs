using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using static Flipper;

public enum RewardType
{
    None = 0,
    Target = 20,
    Drain = -10,
    OtherSide = -5,
    ResetZone = -1,
    Timeout = -5
}

public struct RewardCount
{
    public Dictionary<RewardType, float> CumulatedRewards;
    public float MeanReward => CumulatedRewards.Values.Average();
    
}

public class FlipperAgent : Agent
{
    static int NextAgentId = 0;

    [SerializeField] TrainingPinballArea trainingPinball;
    [SerializeField] Flipper.FlipperSide side = Flipper.FlipperSide.Left;

    int id = 0;
    int episodeId = 0;

    PlayerController playerController;

    float buttonPressedTimestamp = 0f;
    float ballInContactTimestamp = 0f;
    float ballEnterFlipperTimestamp = 0f;
    float ballEnterOtherFlipperTimestamp = 0f;
    float episodeBeginTimestamp = 0f;

    bool BallInContact =>
        Time.fixedTime <=
        ballInContactTimestamp +
        Time.fixedDeltaTime *
        3; // Solves jittering at low speed. We allow 3 fixed update of before counting a new collision as an "OnCollisionEnter"    

    void Awake()
    {
        playerController = new PlayerController();
        playerController.Enable();
        id = NextAgentId++;
    }

    void Start()
    {
        trainingPinball.Flipper(side).onBallEnter += HandleOnBallEnterFlipper;
        trainingPinball.Flipper(side).onBallStay += HandleOnBallInContactWithFlipper;
        trainingPinball.Flipper(side).onBallExit += HandleOnBallExitFlipper;
        if (trainingPinball.LeftFlipper)
        {
            trainingPinball.LeftFlipper.onBallEnter += HandleOnBallEnterAnyFlipper;
        }

        if (trainingPinball.RightFlipper)
        {
            trainingPinball.RightFlipper.onBallEnter += HandleOnBallEnterAnyFlipper;
        }

        trainingPinball.onBallEnterBound += HandleOnBallEnterBound;
        trainingPinball.onBallHitTarget += HandleOnBallHitTarget;
        trainingPinball.onEpisodeEnd += HandleOnEpisodeEnd;
        trainingPinball.onTimeOut += HandleOnTimeout;
    }

    private void OnDestroy()
    {
        trainingPinball.Flipper(side).onBallEnter -= HandleOnBallEnterFlipper;
        trainingPinball.Flipper(side).onBallStay -= HandleOnBallInContactWithFlipper;
        trainingPinball.Flipper(side).onBallExit -= HandleOnBallExitFlipper;
        if (trainingPinball.LeftFlipper)
        {
            trainingPinball.LeftFlipper.onBallEnter -= HandleOnBallEnterAnyFlipper;
        }

        if (trainingPinball.RightFlipper)
        {
            trainingPinball.RightFlipper.onBallEnter -= HandleOnBallEnterAnyFlipper;
        }

        trainingPinball.onBallEnterBound -= HandleOnBallEnterBound;
        trainingPinball.onBallHitTarget -= HandleOnBallHitTarget;
        trainingPinball.onEpisodeEnd -= HandleOnEpisodeEnd;
        trainingPinball.onTimeOut -= HandleOnTimeout;
    }

    private void FixedUpdate()
    {
    }

    public override void OnEpisodeBegin()
    {
        trainingPinball.BeginEpisode();
        buttonPressedTimestamp = 0f;
        ballInContactTimestamp = 0f;
        ballEnterFlipperTimestamp = 0f;
        ballEnterOtherFlipperTimestamp = 0;
        episodeBeginTimestamp = Time.fixedTime;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(GetFlipperToBallVector());
        sensor.AddObservation(GetTargetPosition());
        sensor.AddObservation(GetFlipperNormalizedAngle());
        sensor.AddObservation(GetDistanceToTarget());
    }
    
    Vector2 GetFlipperToBallVector()
    {
        Vector2 result = trainingPinball.Ball.transform.position - trainingPinball.Flipper(side).transform.position;
        result = result.normalized * trainingPinball.Configuration.AveragePinballDimension;
        result = trainingPinball.Flipper(side).WorldToLocalVector(result);

        return result;
    }

    Vector2 GetTargetPosition()
    {
        return trainingPinball.Target.transform.position;
    }

    float GetFlipperNormalizedAngle()
    {
        float result = trainingPinball.Flipper(side).CurrentAngleOffset - trainingPinball.Flipper(side).InitialAngle;
        result /= trainingPinball.Flipper(side).FinalAngle - trainingPinball.Flipper(side).InitialAngle;

        return result;
    }

    float GetDistanceToTarget()
    {
        return (trainingPinball.Target.transform.position - trainingPinball.Ball.transform.position).magnitude;
    }
    
    public override void OnActionReceived(ActionBuffers actions)
    {
        bool isPressed = actions.DiscreteActions[0] > 0;

        if (!trainingPinball.IsButtonPressed(side) && isPressed)
        {
            buttonPressedTimestamp = Time.fixedTime;
        }

        trainingPinball.SetButtonState(isPressed, side);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        bool isPressed = side == Flipper.FlipperSide.Left
            ? playerController.Player.LeftFlipper.IsPressed()
            : playerController.Player.RightFlipper.IsPressed();
        discreteActionsOut[0] = isPressed ? 1 : 0;
    }

    void HandleOnEpisodeEnd()
    {
        Debug.Log(GetCumulativeReward());
        EndEpisode();
        episodeId++;
    }

    void HandleOnBallEnterFlipper(FlipperSide side)
    {
        // Solves jittering at low speed. We allow 3 fixed update of before counting a new collision as an "OnCollisionEnter"
        if (!BallInContact)
        {
            ballEnterFlipperTimestamp = Time.fixedTime;
            //Debug.Log("HandleOnBallEnterFlipper");
        }

        HandleOnBallInContactWithFlipper(side);
    }

    void HandleOnBallInContactWithFlipper(FlipperSide side)
    {
        ballInContactTimestamp = Time.fixedTime;
    }

    void HandleOnBallExitFlipper(FlipperSide side)
    {
        // Do not use. May be called multiple times due to jittering
    }

    void HandleOnBallHitTarget()
    {
        AddReward((int)RewardType.Target);
    }

    void HandleOnBallEnterAnyFlipper(FlipperSide side)
    {
        if (side != this.side)
        {
            ballEnterOtherFlipperTimestamp = Time.fixedTime;
        }
    }

    void HandleOnBallEnterBound(BoundTrigger.BoundType type)
    {
        switch (type)
        {
            case BoundTrigger.BoundType.Drain:
                AddReward((int)RewardType.Drain);
                break;
            case BoundTrigger.BoundType.OtherSide:
                AddReward((int)RewardType.OtherSide);
                break;
            case BoundTrigger.BoundType.ResetZone:
                AddReward((int)RewardType.ResetZone);
                break;
            default:
                break;
        }
    }

    void HandleOnTimeout()
    {
        AddReward((int)RewardType.Timeout);
    }
}