namespace Toybox.Art
{
    /// <summary>
    /// The three quality tiers of ART_BIBLE 7.3. Medium is the reference. The tier that is in force is
    /// decided by the pipeline (Render/Quality) and published through PresentationContext.Quality; it
    /// lives here because materials depend on it (detail bump and the glass back shell).
    /// </summary>
    public enum QualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }
}
