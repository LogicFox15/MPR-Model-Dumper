using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class BakedLightingPriorityModifierMP1
    {
        public CommonObjectData commonObjectData = new CommonObjectData();

        public uint priority;

        public static BakedLightingPriorityModifierMP1 Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            BakedLightingPriorityModifierMP1 script = new BakedLightingPriorityModifierMP1();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildBakedLightingPriorityModifierMP1Properties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static void BuildBakedLightingPriorityModifierMP1Properties(FileReader reader, BakedLightingPriorityModifierMP1 script)
        {
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                ReadBakedLightingPriorityModifierMP1Properties(reader, script);
            }
        }

        public static void ReadBakedLightingPriorityModifierMP1Properties(FileReader reader, BakedLightingPriorityModifierMP1 script)
        {
            var propertyId = reader.ReadUInt32();
            var propertySize = reader.ReadUInt16();

            byte[] propertyData = propertySize > 0
                ? reader.ReadBytes(propertySize)
                : Array.Empty<byte>();

            using MemoryStream ms = new MemoryStream(propertyData);
            using FileReader propertyReader = new FileReader(ms);

            switch (propertyId)
            {
                case 0x54446d42:    // Nested CHPR container
                    BuildBakedLightingPriorityModifierMP1Properties(propertyReader, script);
                    break;
                case 0x8582d268:    // Generic Model
                    script.priority = propertyReader.ReadUInt32();
                    break;
                default:
                    break;
            }
        }
    }
}
