using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class TesteurIA : MonoBehaviour
{
    [SerializeField] int nbTirsTest = 1000;      // nombre de tirs joués avant de conclure
    [SerializeField] bool arreterLePlay = true; // arrêter le Play automatiquement à la fin du test
    

    float scoreAbattre = 0f;        // score du prof en %, choisi automatiquement dans Start()
    bool testTermine = false;       // pour ne donner le résultat qu'une seule fois
    int prochainPalier = 100;       // prochain nombre de tirs auquel on affiche la progression
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreerAutomatiquement()
    {
        if (FindFirstObjectByType<FlipperAgent>() == null) { return; } // pas d'agent dans la scène : rien à tester
        if (FindFirstObjectByType<TesteurIA>() != null) { return; }    // un testeur existe déjà : pas de doublon

        new GameObject("TesteurIA (auto)").AddComponent<TesteurIA>(); // objet temporaire qui porte le testeur
    }

    void Start()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // "Dual" est testé en premier, au cas où le nom contiendrait plusieurs de ces mots
        if (scene.Contains("Dual"))      { scoreAbattre = 32f; } // plateau à deux flippers
        else if (scene.Contains("Hard")) { scoreAbattre = 50f; } // balle aléatoire
        else if (scene.Contains("Easy")) { scoreAbattre = 75f; } // balle fixe
        else { Debug.LogWarning($"TesteurIA : scène '{scene}' inconnue, score à battre = 0"); }
    }

    void Update()
    {
        // Entraînement en cours (Python connecté) : on ne touche à rien
        if (Academy.Instance.IsCommunicatorOn) { return; }

        // Résultat déjà donné : plus rien à faire
        if (testTermine) { return; }

        // Statistiques de tous les agents de la scène
        CompteurRecompenses compteur = FlipperAgent.CompteurEnvironnement;

        // Progression tous les 100 tirs, pour savoir où en est le test
        if (compteur.NbEpisodes >= prochainPalier && compteur.NbEpisodes < nbTirsTest)
        {
            Debug.Log($"TesteurIA : {compteur.NbEpisodes}/{nbTirsTest} tirs | score actuel : {compteur.Pourcentage(TypeRecompense.CibleTouchee):F1} %");
            prochainPalier += 100;
        }

        // Pas encore assez de tirs joués : on attend
        if (compteur.NbEpisodes < nbTirsTest) { return; }

        testTermine = true;

        // Score = pourcentage de tirs qui ont touché la cible
        float score = compteur.Pourcentage(TypeRecompense.CibleTouchee);
        string verdict = score > scoreAbattre ? "BATTU" : "PAS ENCORE"; // strictement au-dessus du prof

        // Nom du modèle testé (lu sur le premier agent trouvé)
        BehaviorParameters parametres = FindFirstObjectByType<BehaviorParameters>();
        string nomModele = (parametres != null && parametres.Model != null) ? parametres.Model.name : "aucun modele";

        // Nom de la config de récompenses utilisée (consigne : conserver la config associée à chaque version)
        FlipperAgent agent = FindFirstObjectByType<FlipperAgent>();
        string nomConfig = agent != null ? agent.NomConfigRecompenses : "aucune config";

        // Nom de la scène testée
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Affichage du résultat dans la console
        Debug.Log($"=== RÉSULTAT DU TEST === {nomModele} ({nomConfig}) | Score : {score:F1} % | À battre : {scoreAbattre} % | {verdict} | {compteur.ResumeParType()}");

        // Fichier récapitulatif de tous les tests (créé avec son en-tête s'il n'existe pas encore)
        string dossier = Path.Combine(Application.dataPath, "..", "Diagnostics");
        Directory.CreateDirectory(dossier);
        string chemin = Path.Combine(dossier, "Resultats.csv");
        if (!File.Exists(chemin))
        {
            File.WriteAllText(chemin, "Date;Scene;Modele;ConfigRecompenses;Tirs;Score;ScoreAbattre;Verdict;Drain;Sortie;TempsEcoule\n");
        }

        // Une ligne par test
        File.AppendAllText(chemin,
            $"{System.DateTime.Now:yyyy-MM-dd HH:mm};{scene};{nomModele};{nomConfig};{compteur.NbEpisodes};{score:F1};{scoreAbattre};{verdict};" +
            $"{compteur.Pourcentage(TypeRecompense.BalleDansDrain):F1};{compteur.Pourcentage(TypeRecompense.BalleSortie):F1};{compteur.Pourcentage(TypeRecompense.TempsEcoule):F1}\n");

#if UNITY_EDITOR
        // Arrêt automatique du Play (ne fonctionne que dans l'éditeur Unity)
        if (arreterLePlay) { UnityEditor.EditorApplication.isPlaying = false; }
#endif
    }
}