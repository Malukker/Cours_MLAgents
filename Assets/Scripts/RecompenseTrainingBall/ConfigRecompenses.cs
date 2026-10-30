using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ConfigRecompenses", menuName = "Scriptable Objects/ConfigRecompenses")]
public class ConfigRecompenses : ScriptableObject
{
    [Serializable]
    public struct LigneRecompense
    {
        public TypeRecompense type;
        public float valeur;
    }
    
    [SerializeField] List<LigneRecompense> lignes = new List<LigneRecompense>();

    public float ValeurDe(TypeRecompense type)
    {
        foreach (LigneRecompense ligne in lignes)
        {
            if (ligne.type == type)
            {
                return ligne.valeur;
            }
        }
        Debug.LogWarning($"Type de récompense non configuré : {type}");
        return 0f;
    }
}
