using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using static Flipper;
using System.IO;

public class FlipperAgent : Agent
{
    static int NextAgentId = 0;

    [SerializeField] TrainingPinballArea trainingPinball;
    [SerializeField] Flipper.FlipperSide side = Flipper.FlipperSide.Left;
    
    // --- Système de diagnostic ---
    [SerializeField] ConfigRecompenses configRecompenses; // le fichier Config
    CompteurRecompenses compteurAgent = new CompteurRecompenses(); // Compteur Propre à l'agent

    [SerializeField] private float intervalleBilan = 10f;
    float prochainBilan = 0f;
    
    [SerializeField] string nomFichierCsv = "Diagnostic_v1";
    string cheminCsv;
    
    static CompteurRecompenses compteurEnvironnement = new CompteurRecompenses();
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ReInitialiserStatique()
    {
        compteurEnvironnement = new CompteurRecompenses();
        NextAgentId = 0;
    }

    int id = 0;
    int episodeId = 0;
    int nbTirsJoues = 0;            // nombre total de tirs terminés
    int nbTirsReussis = 0;          // nombre de tirs où la cible a été touchée
    bool cibleToucheeCeTir = false; // la cible a-t-elle été touchée pendant le tir en cours ?
    bool evenementCeTir = false;

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

        if (id == 0)
        {
            string dossier = Path.Combine(Application.dataPath, "..", "Diagnostics");
            Directory.CreateDirectory(dossier);
            
            cheminCsv = Path.Combine(dossier, $"{nomFichierCsv}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");
            
            File.WriteAllText(cheminCsv, compteurEnvironnement.EnteteCsv() + "\n");
            Debug.Log($"Diagnostic CSV : {Path.GetFullPath(cheminCsv)}");
        }
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
    }

    private void FixedUpdate()
    {
        if(id != 0) {return;}
        
        if(Time.fixedTime < prochainBilan) {return;}
        
        Debug.Log($"=== BILAN ENVIRONNEMENT (t = {Time.fixedTime:F0} s) === Moyenne période : {compteurEnvironnement.MoyennePeriode():F2} | Moyenne totale : {compteurEnvironnement.MoyenneTotale:F2} | Épisodes : {compteurEnvironnement.NbEpisodes}");
        
        Debug.Log($"=== BILAN DÉTAIL === {compteurEnvironnement.ResumeParType()}");
        
        File.AppendAllText(cheminCsv, compteurEnvironnement.LigneCsv(Time.fixedTime) + "\n");
        compteurEnvironnement.NouvellePeriode();
        prochainBilan = Time.fixedTime + intervalleBilan;
    }

    public override void OnEpisodeBegin()
    {
        trainingPinball.BeginEpisode();
        buttonPressedTimestamp = 0f;
        ballInContactTimestamp = 0f;
        ballEnterFlipperTimestamp = 0f;
        ballEnterOtherFlipperTimestamp = 0;
        episodeBeginTimestamp = Time.fixedTime;
        cibleToucheeCeTir = false; // Nouveau tir : la cible n'a pas encore été touchée
        evenementCeTir = false;
    }

    Vector2 VueDepuisFlipper(Vector2 positionMonde)
    {
        Flipper monFlipper = trainingPinball.Flipper(side);                                          // le flipper de cet agent
        Vector2 flipperVersPoint = positionMonde - (Vector2)monFlipper.transform.position;           // vecteur flipper -> point
        Vector2 pointVuDuFlipper = monFlipper.WorldToLocalVector(flipperVersPoint);                  // dans le repère du flipper
        Vector2 pointEnCm = pointVuDuFlipper * trainingPinball.Configuration.CentimeterPerUnit;      // conversion en cm
        return pointEnCm / trainingPinball.Configuration.AveragePinballDimension;                    // normalisation
    }
    
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(VueDepuisFlipper(trainingPinball.Ball.position));
        
        sensor.AddObservation(VueDepuisFlipper((trainingPinball.Target.transform.position)));
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

    // Donne une récompense à l'agent et la Comptabilise
    void DonnerRecompense(TypeRecompense type)
    {
        float valeur = configRecompenses.ValeurDe(type); // lit la valeur dans config
        AddReward(valeur); // donne à l'agent 
        compteurAgent.Enregistrer(type, valeur); // compte pour le diagnostic
        compteurEnvironnement.Enregistrer(type, valeur);
    }

    void HandleOnEpisodeEnd()
    {
        if (!evenementCeTir)
        {
            DonnerRecompense(TypeRecompense.TempsEcoule);
        }
        
        // On lit le total des récompenses du tir AVANT de terminer l'épisode
        float scoreDuTir = GetCumulativeReward();

        //quel agent, quel tir, et le score obtenu
        //Debug.Log($"[Agent {id}] Tir n°{episodeId} -> score : {scoreDuTir}");
        
        nbTirsJoues++;

        // Si la cible a été touchée pendant ce tir, c'est une réussite
        if (cibleToucheeCeTir)
        {
            nbTirsReussis++;
        }

        // Pourcentage de réussite
        float tauxReussite = (float)nbTirsReussis / nbTirsJoues * 100f;

        // Affichage : réussis / joués et pourcentage avec 1 chiffre après la virgule
        // Debug.Log($"[Agent {id}] Réussite : {nbTirsReussis}/{nbTirsJoues} = {tauxReussite:F1} %");

        // Vérifications : le compteur doit trouver le même total que ML-Agent
        // Debug.Log($"[Agent {id}] ML-Agents : {scoreDuTir} | Notre compteur : {compteurAgent.CumulEpisode}");
        
        // on termine notre episode dans le compteur
        compteurAgent.TerminerEpisode();
        
        // Comparaison agent / environnement, avec la moyenne par épisode
        // Debug.Log($"[Agent {id}] Cumul agent : {compteurAgent.CumulTotal} (moy. {compteurAgent.MoyenneTotale:F2}, {compteurAgent.NbEpisodes} ép.) | Cumul environnement : {compteurEnvironnement.CumulTotal} (moy. {compteurEnvironnement.MoyenneTotale:F2}, {compteurEnvironnement.NbEpisodes} ép.)");
        compteurEnvironnement.TerminerEpisode();
        
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
        DonnerRecompense(TypeRecompense.CibleTouchee); // la valeur viens de la config
        cibleToucheeCeTir = true; // On retient que ce tir a touché la cible
        evenementCeTir =  true;
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
        evenementCeTir =  true;
        switch (type)
        {
            case BoundTrigger.BoundType.Drain:
                DonnerRecompense(TypeRecompense.BalleDansDrain);
                break;
            case BoundTrigger.BoundType.OtherSide:
            case BoundTrigger.BoundType.ResetZone:
                DonnerRecompense(TypeRecompense.BalleSortie);
                break;
            default:
                break;
        }
    }
}