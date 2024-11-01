namespace Protocol16
{
    static class Protocol16StreamExtensions
    {
        public static void WriteTypeCodeIfTrue(this Protocol16Stream output, Protocol16Type type, bool writeTypeCode)
        {
            if (writeTypeCode)
            {
                output.WriteByte((byte)type);
            }
        }
    }
}
