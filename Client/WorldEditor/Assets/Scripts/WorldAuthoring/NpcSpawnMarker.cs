using System;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor.Authoring
{
public sealed class NpcSpawnMarker : MonoBehaviour
{
    [SerializeField] private string npcId = string.Empty;
    [SerializeField] private string displayName = "New NPC";
    [SerializeField] private string primaryRole = "merchant";
    [SerializeField] private string servicesCsv = "shop";
    [SerializeField] private string greetingText = "Welcome to Rise of Heroes.";

    public string NpcId => npcId;
    public string DisplayName => displayName;
    public string PrimaryRole => primaryRole;
    public string ServicesCsv => servicesCsv;
    public string GreetingText => greetingText;

    private void Reset()
    {
        EnsureId();
    }

    private void OnValidate()
    {
        EnsureId();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "New NPC";
        }

        if (string.IsNullOrWhiteSpace(primaryRole))
        {
            primaryRole = "merchant";
        }

        if (string.IsNullOrWhiteSpace(servicesCsv))
        {
            servicesCsv = primaryRole;
        }
    }

    private void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            npcId = "npc-" + Guid.NewGuid().ToString("N")[..8];
        }
    }
}
}
