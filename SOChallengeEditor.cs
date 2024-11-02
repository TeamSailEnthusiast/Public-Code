using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SOChallenge))]
public class SOChallengeEditor : Editor
{
    string lastSubTypeName;
    int lastGoalAmount;
    
    public override void OnInspectorGUI()
    {
        SOChallenge challenge = (SOChallenge)target;

        // Store the initial values of SelectedSubType name and GoalAmount for change detection
        if (lastSubTypeName == null)
            lastSubTypeName = challenge.SelectedSubType.name;
        
        if (lastGoalAmount == 0)
            lastGoalAmount = challenge.GoalAmount;

        // Display the Challenge Type field
        challenge.Type = (ChallengeTypes)EditorGUILayout.EnumPopup("Challenge Type", challenge.Type);

        // Contextual SubType dropdown based on Challenge Type
        challenge.SelectedSubType = DisplaySubTypeDropdown(challenge);

        // Goal Amount
        challenge.GoalAmount = EditorGUILayout.IntField("Goal Amount", challenge.GoalAmount);
        
        GUILayout.Space(24);

        challenge.Reward = DisplayReward(challenge.Reward);

        // Manage Required Previous Challenges
        UpdateRequiredPreviousChallenges(challenge);

        GUILayout.Space(24);

        EditorGUILayout.LabelField("Required Previous Challenges", EditorStyles.boldLabel);
        foreach (var requiredChallenge in challenge.RequiredPreviousChallenges)
        {
            EditorGUILayout.LabelField(requiredChallenge.ChallengeName);
        }

        
        // Detect changes to SubType name or GoalAmount and update the filename
        if ((challenge.SelectedSubType.name != lastSubTypeName || challenge.GoalAmount != lastGoalAmount) && !string.IsNullOrEmpty(challenge.SelectedSubType.name))
        {
            string newAssetName = $"{challenge.SelectedSubType.name}{challenge.GoalAmount}";
            string assetPath = AssetDatabase.GetAssetPath(challenge);
            AssetDatabase.RenameAsset(assetPath, newAssetName);

            // Update the last known values after renaming
            lastSubTypeName = challenge.SelectedSubType.name;
            lastGoalAmount = challenge.GoalAmount;
            
            // Save the updated asset
            EditorUtility.SetDirty(challenge);
            AssetDatabase.SaveAssets();
        }


        // Save changes
        if (GUI.changed)
        {
            EditorUtility.SetDirty(challenge);
        }

        // Call the base class method to draw other fields
        // DrawDefaultInspector();
    }

private SubType DisplaySubTypeDropdown(SOChallenge challenge)
{
    string[] options;

    switch (challenge.Type)
    {
        case ChallengeTypes.Woodcutting:
            options = Enum.GetNames(typeof(TreeType));
            break;
        case ChallengeTypes.Fishing:
            options = Enum.GetNames(typeof(FishType));
            break;
        case ChallengeTypes.Mining:
            options = Enum.GetNames(typeof(OreType));
            break;
        case ChallengeTypes.Combat:
            options = Enum.GetNames(typeof(EnemyType));
            break;
        default:
            options = Array.Empty<string>();
            break;
    }

    // Set selectedIndex based on the currently selected name
    int selectedIndex = Array.IndexOf(options, challenge.SelectedSubType.name);
    
    // If the selected name is not found, reset to -1
    if (selectedIndex == -1 && options.Length > 0)
    {
        selectedIndex = 0; // Default to the first option if none is selected
    }

    // Show the dropdown and get the new selected index
    selectedIndex = EditorGUILayout.Popup("Sub Type", selectedIndex, options);

    // Create the selected SubType only if selectedIndex is valid
    SubType selectedSubType = new SubType();
    if (selectedIndex >= 0 && selectedIndex < options.Length)
    {
        selectedSubType.name = options[selectedIndex];
        selectedSubType.id = selectedIndex; // Update logic to assign IDs if needed
    }
    else
    {
        selectedSubType.name = "None"; // Handle case where no valid option is selected
        selectedSubType.id = -1; // Set a default or invalid ID
    }

    return selectedSubType;
}



    private void UpdateRequiredPreviousChallenges(SOChallenge challenge)
    {        
        // Clear the list before populating
        challenge.RequiredPreviousChallenges.Clear();

        // Get all SOChallenges in the project
        var allChallenges = Resources.FindObjectsOfTypeAll<SOChallenge>();

        foreach (var prevChallenge in allChallenges)
        {
            // Check for same type, same subtype, and lower goal amount
            if (prevChallenge.Type == challenge.Type &&
                prevChallenge.SelectedSubType.name == challenge.SelectedSubType.name && // Assuming SubType has a name property
                prevChallenge.GoalAmount < challenge.GoalAmount)
            {
                challenge.RequiredPreviousChallenges.Add(prevChallenge);
            }
        }
    }

private Reward DisplayReward(Reward reward)
{
    EditorGUILayout.LabelField("Experience Rewards", EditorStyles.boldLabel);
    
    if(reward == null) 
        reward = new Reward();

    // Display Exp Rewards
    // EditorGUILayout.LabelField("Experience");
    if (reward.ExpRewards != null)
    {
        for (int i = 0; i < reward.ExpRewards.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("Skill", GUILayout.Width(50)); // Skill label
            reward.ExpRewards[i].Skill = (SkillTypes)EditorGUILayout.EnumPopup(reward.ExpRewards[i].Skill, GUILayout.Width(150)); 
            
            EditorGUILayout.LabelField("|         Exp:", GUILayout.Width(60)); // Experience label
            reward.ExpRewards[i].Exp = EditorGUILayout.IntField(reward.ExpRewards[i].Exp, GUILayout.Width(80)); // IntField for Experience


            if (GUILayout.Button("Remove"))
            {
                reward.ExpRewards.RemoveAt(i);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    GUILayout.Space(4);

    if (GUILayout.Button("Add Experience Reward", GUILayout.Width(204)))
    {
        reward.ExpRewards.Add(new ExpReward());
    }
    
    GUILayout.Space(14);

    // Display Item Rewards
    EditorGUILayout.LabelField("Item Rewards", EditorStyles.boldLabel);
    if (reward.ItemRewards != null)
    {
        for (int i = 0; i < reward.ItemRewards.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Set the width of the ObjectField for SOItem and add some padding
            GUILayout.Label("Item", GUILayout.Width(50));
            reward.ItemRewards[i].Item = (SOItem)EditorGUILayout.ObjectField(reward.ItemRewards[i].Item, typeof(SOItem), false, GUILayout.Width(150));


            // Set the width of the IntField for Amount and add some padding
            GUILayout.Label("| Amount:", GUILayout.Width(60)); // Label width can be adjusted
            reward.ItemRewards[i].Amount = EditorGUILayout.IntField(reward.ItemRewards[i].Amount, GUILayout.Width(80));

            if (GUILayout.Button("Remove"))
            {
                reward.ItemRewards.RemoveAt(i);
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }

    GUILayout.Space(4);

    if (GUILayout.Button("Add Item Reward", GUILayout.Width(204)))
    {
        reward.ItemRewards.Add(new ItemReward()); // Create a new ItemReward with default values
    }

    GUILayout.Space(14);

    EditorGUILayout.BeginHorizontal();

    EditorGUILayout.LabelField("Chest Type Icon: ", EditorStyles.boldLabel);
    reward.ChestType = (ChestTypes)EditorGUILayout.EnumPopup(reward.ChestType);

    EditorGUILayout.EndHorizontal();

    return reward;
}


}
