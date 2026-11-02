using System.Collections.Generic;

namespace TS3Modding.PilotReadiness
{
    public static class ReadinessPolicy
    {
        public static string Refusal(IEnumerable<string> commands, bool pushRequested, bool guardPush)
        {
            foreach (var command in commands)
            {
                if (guardPush && !pushRequested && (command == "PUSHBACK" || command == "APPROVED" || command == "PUSHBACK_DIRECTION" || command == "APPROVED_DIRECTION"))
                    return "Unable, we are not ready for push and start.";

            }
            return null;
        }
    }
}


