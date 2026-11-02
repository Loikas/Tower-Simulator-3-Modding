using System;
using System.Collections.Generic;
namespace TS3Modding.FlightStrips
{
    internal sealed class StripOperationGuard
    {
        private readonly HashSet<int> held=new HashSet<int>();
        internal bool Run(int instance,Action operation,Action<Exception> failed)
        {
            if(held.Contains(instance))return false;
            try{operation();return true;}
            catch(Exception error){held.Add(instance);failed(error);return false;}
        }
        internal void Retain(IEnumerable<int> instances){var live=new HashSet<int>(instances);held.RemoveWhere(id=>!live.Contains(id));}
    }
}
