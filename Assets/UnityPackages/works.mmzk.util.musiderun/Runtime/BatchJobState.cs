namespace Works.Mmzk.Util.Musiderun
{
    public enum BatchJobState
    {
        Idle,
        Queued,
        MirrorCreating,
        MirrorDestroying,
        Syncing,
        Running,
        Completed,
        Failed,
        Skipped
    }
}
