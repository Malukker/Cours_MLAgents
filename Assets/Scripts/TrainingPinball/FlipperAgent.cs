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
    
    [SerializeField] float vitesseMaxBalle = 200f; // vitesse de la balle
    
    [SerializeField] bool afficherDetailsConsole = false;
    
    float distanceMinCible = float.MaxValue;           // plus petite distance balle-cible pendant le tir (en cm)
    bool balleFrappee = false;                         // la balle a-t-elle touché mon flipper pendant ce tir ?
    
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

    private void FixedUpdate()
    {
        // Pour chaque agent : après la frappe, on mémorise la distance la plus courte entre la balle et la cible
        if (balleFrappee)
        {
            float distance = Vector2.Distance(trainingPinball.Ball.position, trainingPinball.Target.transform.position)
                             * trainingPinball.Configuration.CentimeterPerUnit; // distance en cm
            distanceMinCible = Mathf.Min(distanceMinCible, distance);         // on garde la plus petite
        }

        // La suite (bilan de l'environnement) n'est faite que par l'agent 0
        if (id != 0) { return; }

        if (Time.fixedTime < prochainBilan) { return; }

        Debug.Log($"=== BILAN ENVIRONNEMENT (t = {Time.fixedTime:F0} s) === Moyenne période : {compteurEnvironnement.MoyennePeriode():F2} | Moyenne totale : {compteurEnvironnement.MoyenneTotale:F2} | Épisodes : {compteurEnvironnement.NbEpisodes}");
        Debug.Log($"=== BILAN DÉTAIL === {compteurEnvironnement.ResumeParType()}");

        File.AppendAllText(cheminCsv, compteurEnvironnement.LigneCsv(Time.fixedTime) + "\n"); // ligne CSV avant la nouvelle période
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
        distanceMinCible = float.MaxValue; // nouveau tir : aucune distance mesurée
        balleFrappee = false;              // nouveau tir : la balle n'a pas encore été frappée
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
        Flipper monFlipper = trainingPinball.Flipper(side); // le flipper contrôlé par cet agent

        // Position de la balle vue depuis le flipper (2 valeurs)
        sensor.AddObservation(VueDepuisFlipper(trainingPinball.Ball.position));

        // Vitesse de la balle dans le repère du flipper (2 valeurs)
        // Pour une vitesse, on tourne seulement le vecteur (pas de soustraction : une vitesse n'a pas de point de départ)
        Vector2 vitesseVueDuFlipper = monFlipper.WorldToLocalVector(trainingPinball.Ball.linearVelocity);
        sensor.AddObservation(vitesseVueDuFlipper * trainingPinball.Configuration.CentimeterPerUnit / vitesseMaxBalle);

        // Position de la cible vue depuis le flipper (2 valeurs)
        sensor.AddObservation(VueDepuisFlipper(trainingPinball.Target.transform.position));

        // Angle actuel du flipper : 0 = baissé, 1 = levé au maximum (1 valeur)
        sensor.AddObservation(monFlipper.CurrentAngleOffset / trainingPinball.Configuration.FlipperAngleOffsetMax);

        // Bouton appuyé ou non : 1 = appuyé, 0 = relâché (1 valeur)
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
        float valeur = configRecompenses.ValeurDe(type) * facteur; // valeur de la config, ajustée par le facteur
        AddReward(valeur);                                         // donne la récompense à l'agent
        compteurAgent.Enregistrer(type, valeur);                   // compte pour le diagnostic de l'agent
        compteurEnvironnement.Enregistrer(type, valeur);           // compte pour le diagnostic de l'environnement

        if (afficherDetailsConsole) { Debug.Log($"[Agent {id}] Récompense {type} : {valeur:F2}"); }
    }

    void HandleOnEpisodeEnd()
    {
        // Aucun événement pendant ce tir : il s'est terminé par la limite de temps
        if (!evenementCeTir)
        {
            DonnerRecompense(TypeRecompense.TempsEcoule);
        }
        
        float distanceMax = configRecompenses.DistanceProximiteMax; // rayon lu dans la config de cette version
        if (!cibleToucheeCeTir && balleFrappee && distanceMinCible < distanceMax)
        {
            float facteur = 1f - distanceMinCible / distanceMax; // 1 = frôle la cible, 0 = à la limite
            DonnerRecompense(TypeRecompense.ProcheCible, facteur);
        }

        // On lit le total des récompenses du tir AVANT de terminer l'épisode (EndEpisode le remet à 0)
        float scoreDuTir = GetCumulativeReward();

        // Statistiques de réussite de cet agent
        nbTirsJoues++;                                   // un tir de plus terminé
        if (cibleToucheeCeTir) { nbTirsReussis++; }      // la cible a été touchée : réussite
        float tauxReussite = (float)nbTirsReussis / nbTirsJoues * 100f; // pourcentage de réussite

        // Consigne 2.1 : afficher le cumul des récompenses en fin d'épisode (+ le taux de réussite de l'agent)
        if (afficherDetailsConsole)
        {
            Debug.Log($"[Agent {id}] Fin du tir n°{episodeId} -> cumul : {scoreDuTir} | Réussite : {nbTirsReussis}/{nbTirsJoues} = {tauxReussite:F1} %");
        }

        // On clôture l'épisode dans les deux compteurs
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
        balleFrappee = true; // la balle a touché mon flipper : on commence à mesurer sa distance à la cible
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