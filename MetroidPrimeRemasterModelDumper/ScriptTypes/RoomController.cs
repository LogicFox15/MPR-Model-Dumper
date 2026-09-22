using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;


namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class RoomController
    {
        public List<SMPRRoomControllerProperty> properties = new List<SMPRRoomControllerProperty>();

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static RoomController Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            RoomController script = new RoomController();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                ushort count = br.ReadUInt16();
                script.properties.Add(SMPRRoomControllerProperty.Read(br));

            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }


    public class SMPRRoomControllerProperty
    {
        public uint propertyId;
        public ushort propertySize;
        public CObjectId roomId;

        public static SMPRRoomControllerProperty Read(FileReader reader)
        {
            SMPRRoomControllerProperty prop = new SMPRRoomControllerProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            if (prop.propertyId == 0x30a4d63d)
            {
                prop.roomId = reader.ReadStruct<CObjectId>();
            }
            else if (prop.propertySize > 0)
            {
                reader.ReadBytes(prop.propertySize);
            }

            return prop;
        }
    }
}
