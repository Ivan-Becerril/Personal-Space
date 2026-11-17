using System;

namespace PersonalSpace
{
    public enum FighterStatus
    {
        Conscious,
        KnockedOut
    }

    internal sealed class FighterCondition
    {
        internal FighterStatus Status { get; private set; }
        internal float Recovery { get; private set; } = 1f;
        private float remaining;

        internal void KnockOut(float duration)
        {
            if (Status == FighterStatus.KnockedOut)
                return;

            Status = FighterStatus.KnockedOut;
            remaining = duration;
            Recovery = 0f;
        }

        internal void Step(float seconds, float recoveryDuration, bool canRecover)
        {
            if (Status == FighterStatus.KnockedOut)
            {
                remaining = Math.Max(0f, remaining - seconds);
                if (remaining <= 0f)
                    Status = FighterStatus.Conscious;
                return;
            }

            if (canRecover)
                Recovery = Math.Min(1f, Recovery + seconds / Math.Max(0.1f, recoveryDuration));
        }
    }
}
