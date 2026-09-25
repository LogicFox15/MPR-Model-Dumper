using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class VolumetricFogRegion
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    public float far;
    public float near;
    public Vector4 color;
    public float unknown;

    public static VolumetricFogRegion Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        VolumetricFogRegion script = new VolumetricFogRegion();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildVolumetricFogRegionProperties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static VolumetricFogRegion prepTransform(VolumetricFogRegion retroObject, ConstructedLayer parsed)
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

    public static void BuildVolumetricFogRegionProperties(FileReader reader, VolumetricFogRegion script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadVolumetricFogRegionProperties(reader, script);
        }
    }

    public static void ReadVolumetricFogRegionProperties(FileReader reader, VolumetricFogRegion script)
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
            case 0xbaf7ac02:    // Far
                script.far = propertyReader.ReadSingle();
                break;
            case 0xd1fc0ce8:    // Near
                script.near = propertyReader.ReadSingle();
                break;
            case 0xc35e3f33:    // Color
                float X = propertyReader.ReadSingle();
                float Y = propertyReader.ReadSingle();
                float Z = propertyReader.ReadSingle();
                float W = propertyReader.ReadSingle();
                script.color = new Vector4(X, Y, Z, W);
                break;
            default:
                break;
        }
    }
}
