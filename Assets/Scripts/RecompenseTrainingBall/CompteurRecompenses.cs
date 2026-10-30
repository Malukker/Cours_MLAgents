using System.Collections.Generic;
using System.Text;
public class CompteurRecompenses
{
    public Dictionary<TypeRecompense, int> nbParType = new Dictionary<TypeRecompense, int>();
    
    public Dictionary<TypeRecompense, float> sommeParType = new Dictionary<TypeRecompense, float>();
    
    public float CumulTotal { get; private set; }
    public float CumulEpisode { get;  private set; }
    public int NbEpisodes {get; private set; }
    
    float cumulAuDernierBilan = 0f;
    int episodesAuDernierBilan = 0;
    
    public float MoyenneTotale => NbEpisodes > 0 ? CumulTotal / NbEpisodes : 0f;

    public string ResumeParType()
    {
        StringBuilder texte = new StringBuilder();

        foreach (TypeRecompense type in System.Enum.GetValues(typeof(TypeRecompense)))
        {
            int nb = nbParType.ContainsKey(type) ? nbParType[type] : 0;

            float pourcentage = NbEpisodes > 0 ? (float)nb / NbEpisodes * 100f : 0f;

            texte.Append($"{type} : {nb} ({pourcentage:F0} %) | ");
        }
        return texte.ToString();
    }
    
    public float MoyennePeriode()
    {
        int episodesPeriode = NbEpisodes - episodesAuDernierBilan;
        float cumulPeriode = CumulTotal - cumulAuDernierBilan;
        return episodesPeriode >  0 ? cumulPeriode / episodesPeriode : 0f;
    }

    public void NouvellePeriode()
    {
        cumulAuDernierBilan = CumulTotal;
        episodesAuDernierBilan = NbEpisodes;
    }

    public void Enregistrer(TypeRecompense type, float valeur)
    {
        if (!nbParType.ContainsKey(type))
        {
            nbParType[type] = 0;
            sommeParType[type] = 0f;
        }
        
        nbParType[type]++;
        sommeParType[type] += valeur;
        CumulTotal += valeur;
        CumulEpisode += valeur;
    }

    public void TerminerEpisode()
    {
        NbEpisodes++;
        CumulEpisode = 0f;
    }

    public string EnteteCsv()
    {
        StringBuilder texte = new StringBuilder("Temps;Episodes;MoyennePeriode;MoyenneTotale;CumulTotal");
        foreach (TypeRecompense type in System.Enum.GetValues(typeof(TypeRecompense)))
        {
            texte.Append($";{type}");
        }
        return texte.ToString();
    }

    public string LigneCsv(float temps)
    {
        StringBuilder texte = new StringBuilder($"{temps:F0}; {NbEpisodes};{MoyennePeriode():F3};{MoyenneTotale:F3};{CumulTotal}");

        foreach (TypeRecompense type in System.Enum.GetValues(typeof(TypeRecompense)))
        {
            int nb = nbParType.ContainsKey(type) ? nbParType[type] : 0;
            texte.Append($";{nb}");
        }
        return texte.ToString();
    }
}
