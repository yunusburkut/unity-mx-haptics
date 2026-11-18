namespace Loupedeck.UnityHapticsPlugin
{
    using System;

    // The plugin service expects one ClientApplication per plugin, even a universal one (as in Logitech's sample).
    public class UnityHapticsApplication : ClientApplication
    {
        protected override String GetProcessName() => "";
        protected override String GetBundleName() => "";
        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
