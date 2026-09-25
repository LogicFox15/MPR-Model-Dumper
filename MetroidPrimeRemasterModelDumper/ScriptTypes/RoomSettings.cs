using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class RoomSettings
    {
        public CommonObjectData commonObjectData;

        public byte unknownRenderFlag;

        public static RoomSettings Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            RoomSettings script = new RoomSettings();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildRoomSettingsProperties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static void BuildRoomSettingsProperties(FileReader reader, RoomSettings script)
        {
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                ReadRoomSettingsProperties(reader, script);
            }
        }

        public static void ReadRoomSettingsProperties(FileReader reader, RoomSettings script)
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
                case 0x71a12a41:    // Nested CHPR container
                    script.unknownRenderFlag = propertyReader.ReadByte();
                    break;
                default:
                    break;
            }
        }
    }
}
