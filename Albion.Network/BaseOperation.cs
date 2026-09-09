using System.Collections.Generic;

namespace Albion.Network
{
    public abstract class BaseOperation
    {
        /// <summary>Server status for responses; zero for request models.</summary>
        public short ReturnCode { get; internal set; }

        public BaseOperation(Dictionary<byte, object> parameters) { }
    }
}
