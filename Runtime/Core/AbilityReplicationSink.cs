namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Replication hook for future multiplayer. Assign an implementation to
    /// <see cref="AbilitySystem.ReplicationSink"/> to forward activation, cue,
    /// and completion events to the netcode layer. The system itself stays
    /// single-player and never blocks gameplay on the sink.
    /// </summary>
    public interface IAbilityReplicationSink
    {
        void OnAbilityActivated(AbilityDefinition ability, AbilityContext context);
        void OnGameplayCue(GameplayTag cue, AbilityContext context);
        void OnAbilityEnded(AbilityDefinition ability, AbilityContext context, bool completed);
    }
}
