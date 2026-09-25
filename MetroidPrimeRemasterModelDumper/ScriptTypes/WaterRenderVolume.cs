using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class WaterRenderVolume
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    public CObjectId waterModel;
    public CObjectId waterNormalMap;
    public Vector4 waterColor;

    public static WaterRenderVolume Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        WaterRenderVolume script = new WaterRenderVolume();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildWaterRenderVolumeProperties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static WaterRenderVolume prepTransform(WaterRenderVolume retroObject, ConstructedLayer parsed)
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

    public static void BuildWaterRenderVolumeProperties(FileReader reader, WaterRenderVolume script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            BuildWaterRenderVolumeProperties(reader, script);
        }
    }

    public static void ReadWaterRenderVolumeProperties(FileReader reader, WaterRenderVolume script)
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
            case 0xd1e9d29d:    // Water Data Chunk
            case 0x03e33f4b:    // Normal Map Chunk
            case 0x7982e07b:    // Water Color Chunk
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0x736e5890:    // Water Model
                script.waterModel = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0x90a143ef:    // Normal Map
                script.waterNormalMap = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0x6f7355a3:    // Water Color
                float X = propertyReader.ReadSingle();
                float Y = propertyReader.ReadSingle();
                float Z = propertyReader.ReadSingle();
                float W = propertyReader.ReadSingle();
                script.waterColor = new Vector4(X, Y, Z, W);
                break;
            default:
                break;
        }
    }
}
