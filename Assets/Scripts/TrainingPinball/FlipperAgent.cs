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
    
    [SerializeField] float vitesseMaxBalle = 200f;
    
    [SerializeField] bool afficherDetailsConsole = false;
    
    float distanceMinCible = float.MaxValue;
    bool balleFrappee = false;
    
    bool balleEnContactAvant = false;   
    float meilleureTrajectoireTir = 0f; 
    const float vitesseMinFrappe = 30f; 
    
    static CompteurRecompenses compteurEnvironnement = new CompteurRecompenses();
    
    // Accès en lecture au compteur de l'environnement
    public static CompteurRecompenses CompteurEnvironnement => compteurEnvironnement;
    
    // Nom du fichier de config de récompenses utilisé 
    public string NomConfigRecompenses => configRecompenses != null ? configRecompenses.name : "aucune config";
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ReInitialiserStatique()
    {
        compteurEnvironnement = new CompteurRecompenses();
        NextAgentId = 0;
    }

    int id = 0;
    int episodeId = 0;
    int nbTirsJoues = 0;
    int nbTirsReussis = 0;
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
        
        prochainBilan = Time.fixedTime + intervalleBilan;
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

    float DistancePrevueALaCible()
    {
        Vector2 position = trainingPinball.Ball.position;                              // point de départ
        Vector2 vitesse = trainingPinball.Ball.linearVelocity;                         // vitesse au départ
        Vector2 gravite = Physics2D.gravity * trainingPinball.Ball.gravityScale;       // gravité appliquée à la balle
        Vector2 cible = trainingPinball.Target.transform.position;                  // centre de la cible

        float distanceMin = float.MaxValue;
        for (int i = 0; i < 100; i++)
        {
            float t = i * 0.02f;
            Vector2 positionPrevue = position + vitesse * t + 0.5f * gravite * t * t;  // formule de la parabole
            distanceMin = Mathf.Min(distanceMin, Vector2.Distance(positionPrevue, cible));
        }
        return distanceMin * trainingPinball.Configuration.CentimeterPerUnit;
    }
    
    private void FixedUpdate()
    {
                bool balleEnContact = BallInContact;
                if (balleEnContactAvant && !balleEnContact)
                {
                    float vitesseCm = trainingPinball.Ball.linearVelocity.magnitude * trainingPinball.Configuration.CentimeterPerUnit;
                    if (vitesseCm > vitesseMinFrappe)
                    {
                        float distanceMax = configRecompenses.DistanceProximiteMax;
                        float distancePrevue = DistancePrevueALaCible();
                        float facteur = Mathf.Clamp01(1f - distancePrevue / distanceMax);
        
                        //récompense que l'amélioration
                        if (facteur > meilleureTrajectoireTir)
                        {
                            DonnerRecompense(TypeRecompense.TrajectoireVersCible, facteur - meilleureTrajectoireTir);
                            meilleureTrajectoireTir = facteur;
                        }
                    }
                }
                balleEnContactAvant = balleEnContact; //mémorise pour l'image suivante
        
        if (balleFrappee)
        {
            float distance = Vector2.Distance(trainingPinball.Ball.position, trainingPinball.Target.transform.position)
                             * trainingPinball.Configuration.CentimeterPerUnit;
            distanceMinCible = Mathf.Min(distanceMinCible, distance);
        }

        if (id != 0) { return; }

        if (Time.fixedTime < prochainBilan) { return; }

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
        cibleToucheeCeTir = false;
        evenementCeTir = false;
        distanceMinCible = float.MaxValue;
        balleFrappee = false;
        balleEnContactAvant = false;
        meilleureTrajectoireTir = 0f;
    }

    Vector2 VueDepuisFlipper(Vector2 positionMonde)
    {
        Flipper monFlipper = trainingPinball.Flipper(side);
        Vector2 flipperVersPoint = positionMonde - (Vector2)monFlipper.transform.position;
        Vector2 pointVuDuFlipper = monFlipper.WorldToLocalVector(flipperVersPoint);
        Vector2 pointEnCm = pointVuDuFlipper * trainingPinball.Configuration.CentimeterPerUnit;
        return pointEnCm / trainingPinball.Configuration.AveragePinballDimension;
    }
    
    public override void CollectObservations(VectorSensor sensor)
    {
        Flipper monFlipper = trainingPinball.Flipper(side);

        //Position de la balle vue depuis le flipper (2 valeurs)
        sensor.AddObservation(VueDepuisFlipper(trainingPinball.Ball.position));

        //Vitesse de la balle dans le repère du flipper (2 valeurs)
        Vector2 vitesseVueDuFlipper = monFlipper.WorldToLocalVector(trainingPinball.Ball.linearVelocity);
        sensor.AddObservation(vitesseVueDuFlipper * trainingPinball.Configuration.CentimeterPerUnit / vitesseMaxBalle);

        //Position de la cible vue depuis le flipper (2 valeurs)
        sensor.AddObservation(VueDepuisFlipper(trainingPinball.Target.transform.position));

        //Angle actuel du flipper : 0 = baissé, 1 = levé au maximum (1 valeur)
        sensor.AddObservation(monFlipper.CurrentAngleOffset / trainingPinball.Configuration.FlipperAngleOffsetMax);

        //Bouton appuyé ou non : 1 = appuyé, 0 = relâché (1 valeur)
        sensor.AddObservation(trainingPinball.IsButtonPressed(side) ? 1f : 0f);
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
    
    void DonnerRecompense(TypeRecompense type, float facteur = 1f)
    {
        float valeur = configRecompenses.ValeurDe(type) * facteur;
        AddReward(valeur);
        compteurAgent.Enregistrer(type, valeur);
        compteurEnvironnement.Enregistrer(type, valeur);

        if (afficherDetailsConsole) { Debug.Log($"[Agent {id}] Récompense {type} : {valeur:F2}"); }
    }

    void HandleOnEpisodeEnd()
    {
        if (!evenementCeTir)
        {
            DonnerRecompense(TypeRecompense.TempsEcoule);
        }
        
        float distanceMax = configRecompenses.DistanceProximiteMax;
        if (!cibleToucheeCeTir && balleFrappee && distanceMinCible < distanceMax)
        {
            float facteur = 1f - distanceMinCible / distanceMax;
            DonnerRecompense(TypeRecompense.ProcheCible, facteur);
        }

        float scoreDuTir = GetCumulativeReward();

        nbTirsJoues++;
        if (cibleToucheeCeTir) { nbTirsReussis++; }
        float tauxReussite = (float)nbTirsReussis / nbTirsJoues * 100f;

        if (afficherDetailsConsole)
        {
            Debug.Log($"[Agent {id}] Fin du tir n°{episodeId} -> cumul : {scoreDuTir} | Réussite : {nbTirsReussis}/{nbTirsJoues} = {tauxReussite:F1} %");
        }

        compteurAgent.TerminerEpisode();
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
        balleFrappee = true;
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
        DonnerRecompense(TypeRecompense.CibleTouchee);
        cibleToucheeCeTir = true;
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