using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class RoomSettings
    {
        public List<SMPRRoomSettingProperty> properties = new List<SMPRRoomSettingProperty>();

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static RoomSettings Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            RoomSettings script = new RoomSettings();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                ushort count = br.ReadUInt16();
                script.properties.Add(SMPRRoomSettingProperty.Read(br));
                
            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }

    public class SMPRRoomSettingProperty
    {
        public uint propertyId;
        public ushort propertySize;
        public byte unknownRenderFlag;

        public static SMPRRoomSettingProperty Read(FileReader reader)
        {
            SMPRRoomSettingProperty prop = new SMPRRoomSettingProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            if (prop.propertyId == 0x71a12a41)
            {
                prop.unknownRenderFlag = reader.ReadByte();
            }
            else if (prop.propertySize > 0)
            {
                reader.ReadBytes(prop.propertySize);
            }

            return prop;
        }
    }
}
