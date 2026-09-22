using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class EntityProperties
    {
        public bool active;
        public bool unknownFlag;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static EntityProperties Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            EntityProperties script = new EntityProperties();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                script.active = br.ReadBoolean();
                script.unknownFlag = br.ReadBoolean();
                script.position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                script.rotation = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                script.scale = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }

    }
}
