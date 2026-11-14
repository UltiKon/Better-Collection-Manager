using UnityEngine;

public sealed class RenderToTexture : Behaviour { }
public sealed class GhostCard : Component
{
    public enum Type { NONE, NOT_VALID }
    public GameObject m_GlowPlane, m_GlowPlaneElite;
    public RenderToTexture m_R2T_EffectGhost;
    public Type GhostType;
    public TAG_PREMIUM Premium;
    public bool Force, UsingRenderTexture;
    public int Initialized, MaterialsApplied;
    public void SetGhostType(Type type) { GhostType = type; }
    public void SetPremium(TAG_PREMIUM premium) { Premium = premium; }
    private void Init(bool force, bool usingRenderToTexture)
    { Force = force; UsingRenderTexture = usingRenderToTexture; Initialized++; }
    private void ApplyGhostMaterials() { MaterialsApplied++; }
}
