using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class VolumetricFog
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    // Put FogWind here
    // Put FogAttenuation here
    // Put Maya Spline here

    public Vector4 color1;
    public Vector4 color2;

    public float optionalUnk1 = 0;
    public float float1 = 0;
    public float float2 = 0;
    public float optionalUnk2 = 0;
    public float float3 = 0;
    public float float4 = 0;
    public float float5 = 0;
    public float optionalUnk3 = 0;
    public float float6 = 0;

    public byte unknownBool;

    public static VolumetricFog Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        VolumetricFog script = new VolumetricFog();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildVolumetricFogProperties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static VolumetricFog prepTransform(VolumetricFog retroObject, ConstructedLayer parsed)
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

    public static void BuildVolumetricFogProperties(FileReader reader, VolumetricFog script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadVolumetricFogProperties(reader, script);
        }
    }

    public static void ReadVolumetricFogProperties(FileReader reader, VolumetricFog script)
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
            case 0x8efd902b:    // contains SLdrVolumetricFogWind
            case 0xac68c5ae:    // contains SLdrVolumetricFogAttenuation
            case 0x7bc5ef27:    // Maya Spline
                if (propertySize > 0)
                {
                    propertyReader.ReadBytes(propertySize);
                }
                break;
            case 0x95e259b4:
                script.optionalUnk1 = propertyReader.ReadSingle();
                break;
            case 0x9c1e9f8f:
                script.float1 = propertyReader.ReadSingle();
                break;
            case 0xbf9b3481:
                script.float2 = propertyReader.ReadSingle();
                break;
            case 0xdd36a237:
                script.optionalUnk2 = propertyReader.ReadSingle();
                break;
            case 0xe020e7c4:
                script.float3 = propertyReader.ReadSingle();
                break;
            case 0xf259966e:
                script.float4 = propertyReader.ReadSingle();
                break;
            case 0x2074d13d:
                script.float5 = propertyReader.ReadSingle();
                break;
            case 0x22096751:
                script.optionalUnk3 = propertyReader.ReadSingle();
                break;
            case 0x33cd9d58:
                script.float6 = propertyReader.ReadSingle();
                break;
            case 0xe3b56cab:
                script.unknownBool = propertyReader.ReadByte();
                break;
            default:
                break;
        }
    }
}
