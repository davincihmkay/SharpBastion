using SharpBastion.Interface;

namespace SharpBastion.Protocol;

/// <summary>
/// Carries the user's explicit consent decision for auto-approving Kagi search
/// proposals made by the model. Defaults to not overridden — every proposal is
/// queued in PendingKagiSearchQueue and requires interactive 'review' approval.
/// Once overridden, proposals execute unattended for the rest of the session,
/// bounded by Assistant:KagiSearchMaxRoundTrips (see AssistantOptions) so an
/// auto-approved chain cannot run indefinitely with no human in the loop.
/// </summary>
public class KagiSearchProtocol : IKagiSearchProtocol
{
    public bool IsOverridden { get; private set; } = false;

    public void OverrideProtocol() => IsOverridden = true;

    public string Subject => "kagi-search restriction and allow unattended searches";

    public string Description =>
        "Model-proposed Kagi searches can run unattended, or wait for interactive approval.\n" +
        "The search query text (crafted by the model) leaves this machine to Kagi's API once a\n" +
        "proposal runs, whether unattended or explicitly approved.";

    public string OverriddenMessage => "Kagi-search restriction: OVERRIDDEN. Proposed searches run unattended.";

    public string ActiveMessage => "Kagi-search restriction: ACTIVE. Proposed searches are queued — approve via 'review'.";
}