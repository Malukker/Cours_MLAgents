using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class TesteurIA : MonoBehaviour
{
    [SerializeField] int nbTirsTest = 1000;
    [SerializeField] bool arreterLePlay = true; // arrêter le Play automatiquement à la fin du test
    

    float scoreAbattre = 0f;
    bool testTermine = false;
    int prochainPalier = 100;
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreerAutomatiquement()
    {
        if (FindFirstObjectByType<FlipperAgent>() == null) { return; }
        if (FindFirstObjectByType<TesteurIA>() != null) { return; }

        new GameObject("TesteurIA (auto)").AddComponent<TesteurIA>();
    }

    void Start()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        if (scene.Contains("Hard")) { scoreAbattre = 50f; } // balle aléatoire
        else if (scene.Contains("Easy")) { scoreAbattre = 75f; } // balle fixe
        else { Debug.LogWarning($"TesteurIA : scène '{scene}' inconnue, score à battre = 0"); }
    }

    void Update()
    {
        // Entraînement en cours (Python connecté)
        if (Academy.Instance.IsCommunicatorOn) { return; }

        // Résultat déjà donné
        if (testTermine) { return; }

        // Statistiques de tous les agents de la scène
        CompteurRecompenses compteur = FlipperAgent.CompteurEnvironnement;

        // Progression tous les 100 tirs, pour savoir où en est le test
        if (compteur.NbEpisodes >= prochainPalier && compteur.NbEpisodes < nbTirsTest)
        {
            Debug.Log($"TesteurIA : {compteur.NbEpisodes}/{nbTirsTest} tirs | score actuel : {compteur.Pourcentage(TypeRecompense.CibleTouchee):F1} %");
            prochainPalier += 100;
        }

        //on attend
        if (compteur.NbEpisodes < nbTirsTest) { return; }

        testTermine = true;

        //pourcentage de tirs qui ont touché la cible
        float score = compteur.Pourcentage(TypeRecompense.CibleTouchee);
        string verdict = score > scoreAbattre ? "BATTU" : "PAS ENCORE";

        // Nom du modèle testé
        BehaviorParameters parametres = FindFirstObjectByType<BehaviorParameters>();
        string nomModele = (parametres != null && parametres.Model != null) ? parametres.Model.name : "aucun modele";

        // Nom de la config de récompenses utilisée
        FlipperAgent agent = FindFirstObjectByType<FlipperAgent>();
        string nomConfig = agent != null ? agent.NomConfigRecompenses : "aucune config";

        // Nom de la scène testée
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Affichage du résultat dans la console
        Debug.Log($"=== RÉSULTAT DU TEST === {nomModele} ({nomConfig}) | Score : {score:F1} % | À battre : {scoreAbattre} % | {verdict} | {compteur.ResumeParType()}");

        // Fichier récapitulatif de tous les tests
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
        // Arrêt automatique du Play
        if (arreterLePlay) { UnityEditor.EditorApplication.isPlaying = false; }
#endif
    }
}