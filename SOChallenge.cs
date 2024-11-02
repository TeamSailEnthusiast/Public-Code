using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ChallengeTypes {
    //Skills
    Mining,
    Fishing,
    Woodcutting,

    Combat,

    Smithing,
    Alchemy,
    Cooking,

    Farming,
    Animalcraft,
    Crafting,

    //Miscellaneous
    Consume,
}


[CreateAssetMenu(fileName = "SOChallenge", menuName = "SO/Journal/SOChallenge")]
public class SOChallenge : ScriptableObject
{
    public List<SOChallenge> RequiredPreviousChallenges = new List<SOChallenge>();

    public ChallengeTypes Type;

    public SubType SelectedSubType;
    public int GoalAmount;

    public Reward Reward; 

    // Automatically gets the name of the asset
    public string ChallengeName => name;

    public bool Completed;

    private static List<SOChallenge> pendingUpdates = new List<SOChallenge>();

    private void OnValidate()
    {
        // Automatically update the RequiredPreviousChallenges list
        UpdateRequiredPreviousChallenges();

        if (!pendingUpdates.Contains(this))
        {
            pendingUpdates.Add(this);
            // Schedule the update call
            UnityEditor.EditorApplication.delayCall += ProcessPendingUpdates;
        }
    }

    private static void ProcessPendingUpdates()
    {
        // Only proceed if there are pending updates
        if (pendingUpdates.Count == 0) return;

        // Process each pending SOChallenge
        foreach (var challenge in pendingUpdates)
        {
            SOChallengeBarUpdater.ProcessChallengeUpdate(challenge);
        }

        // Clear the pending updates list after processing
        pendingUpdates.Clear();
    }
    private void UpdateRequiredPreviousChallenges()
    {
        // Clear the list before populating
        RequiredPreviousChallenges.Clear();

        // Get all SOChallenges in the project
        var allChallenges = Resources.FindObjectsOfTypeAll<SOChallenge>();

        foreach (var prevChallenge in allChallenges)
        {
            // Check for same type, same subtype, and lower goal amount
            if (prevChallenge.Type == Type &&
                prevChallenge.SelectedSubType.name == SelectedSubType.name && // Assuming SubType has a name property
                prevChallenge.GoalAmount < GoalAmount)
            {
                RequiredPreviousChallenges.Add(prevChallenge);
            }
        }
    }
}
[System.Serializable]
public class SubType {
    public string name;
    public int id;
}

[System.Serializable]
public class Reward {
    public ChestTypes ChestType;
    public List<ExpReward> ExpRewards = new();
    public List<ItemReward> ItemRewards = new();
}

[System.Serializable]
public class ExpReward {
    public SkillTypes Skill;
    public int Exp;
}
[System.Serializable]
public class ItemReward {
    public SOItem Item;
    public int Amount = 1;
}

public enum ChestTypes {
    EasyWood,
    MediumGreen,
    HardOrange,
    VeryHardRed,
    EliteTrophy
}
