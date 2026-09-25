using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class Skybox
    {
        public CommonObjectData commonObjectData = new CommonObjectData();

        public CObjectId objectID;
        public float skyboxStrength;

        public static Skybox Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            Skybox script = new Skybox();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader reader = new FileReader(ms))
            {
                BuildSkyboxProperties(reader, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static Skybox prepTransform(Skybox retroObject, ConstructedLayer parsed)
        {
            foreach (var prop in parsed.entityProperties)
            {
                foreach (var link in prop.originalInstanceData.links)
                {
                    if (link.target.ToString() == retroObject.commonObjectData.originalInstanceData.id.ToString())
                    {
                        retroObject.commonObjectData.entityProperties = prop;
                        break;
                    }
                }
            }
            return retroObject;
        }

        public static void BuildSkyboxProperties(FileReader reader, Skybox script)
        {
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                ReadSkyboxProperties(reader, script);
            }
        }

        public static void ReadSkyboxProperties(FileReader reader, Skybox script)
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
                case 0x387bb786: // Skybox Model
                    script.objectID = propertyReader.ReadStruct<CObjectId>();
                    break;
                case 0x63328a04: // Skybox Strength?
                    script.skyboxStrength = propertyReader.ReadSingle();
                    break;
                default:
                    break;
            }
        }
    }
}

