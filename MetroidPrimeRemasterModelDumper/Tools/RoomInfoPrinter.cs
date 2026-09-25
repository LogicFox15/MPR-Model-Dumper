using DKCTF;
using IONET.Collada.Kinematics.Articulated_Systems;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MetroidPrimeRemasterModelDumper
{
    public class RoomInfoPrinter
    {
        public static void PrintStaticLights(string path, ConstructedRoom room)
        {
            // Inside ObjectInfoParser.cs, where you normally write your .txt file:
            var exportLights = new List<object>();

            foreach (var layer in room.layers)
            {
                foreach (var light in layer.lightStatics)
                {
                    // 1. Grab the base Blender rotation we already calculated
                    Vector3 finalRotation = light.commonObjectData.entityProperties.blenderRotation;   

                    exportLights.Add(new
                    {
                        Position = new[] { light.commonObjectData.entityProperties.position.X, light.commonObjectData.entityProperties.position.Y, light.commonObjectData.entityProperties.position.Z },
                        Rotation = new[] { finalRotation.X, -finalRotation.Z, finalRotation.Y },
                        Scale = new[] { light.commonObjectData.entityProperties.scale.X, light.commonObjectData.entityProperties.scale.Y, light.commonObjectData.entityProperties.scale.Z },
                        InnerRadius = light.innerRadius,
                        OuterRadius = light.outerRadius,
                        InnerAngleDegrees = light.innerAngleDegrees,
                        OuterAngleDegrees = light.outerAngleDegrees,
                        Color = new[] { light.color.X, light.color.Y, light.color.Z },
                        LumaIntensity = light.lumaIntensity,
                        DistanceMode = light.distanceMode,
                        CastShadows = light.castShadows,
                        Type = light.lightType,
                        Baked = light.baked
                    });
                }
            }

            string jsonOutput = JsonSerializer.Serialize(exportLights, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path + "_lights.json", jsonOutput);
        }

        public static void PrintParsedObjects(string path, ConstructedRoom room)
        {
            string ObjectTXT = "Room Object Information: ";

            for (int i = 0; i < room.layers.Count; i++)
            {
                ObjectTXT += System.Environment.NewLine + "Layer " + i + ": " + room.layers[i].name;

                if (room.layers[i].roomControllers.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Room Controllers: ";
                }
                foreach (var retroObject in room.layers[i].roomControllers)
                {
                    ObjectTXT += System.Environment.NewLine + "Room ID: " + RoomController.GetNameFromId(retroObject.roomId.ToString());
                    ObjectTXT += System.Environment.NewLine + "Position: " + retroObject.commonObjectData.entityProperties.position.X.ToString() + ", " + (-retroObject.commonObjectData.entityProperties.position.Z).ToString() + ", " + retroObject.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + retroObject.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + retroObject.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + retroObject.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + retroObject.commonObjectData.entityProperties.scale.X.ToString() + ", " + retroObject.commonObjectData.entityProperties.scale.Z.ToString() + ", " + retroObject.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine;
                }

                // Print the Modular Construction containers.
                if (room.layers[i].modCons.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "ModCons: ";
                }
                foreach (var modcon in room.layers[i].modCons)
                {
                    ObjectTXT += System.Environment.NewLine + "ModCon ID: " + modcon.modularConstructionId.ToString();
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Position: " + modcon.commonObjectData.entityProperties.position.X.ToString() + ", " + (-modcon.commonObjectData.entityProperties.position.Z).ToString() + ", " + modcon.commonObjectData.entityProperties.position.Y.ToString();
                        ObjectTXT += System.Environment.NewLine + "Rotation: " + modcon.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + modcon.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + modcon.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                        ObjectTXT += System.Environment.NewLine + "Scale: " + modcon.commonObjectData.entityProperties.scale.X.ToString() + ", " + modcon.commonObjectData.entityProperties.scale.Z.ToString() + ", " + modcon.commonObjectData.entityProperties.scale.Y.ToString();
                    }
                    catch
                    {

                    }
                    ObjectTXT += System.Environment.NewLine;
                }

                // Print the Skybox if there is one.
                if (room.layers[i].skyboxes.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Skyboxes: ";
                }
                foreach (var skybox in room.layers[i].skyboxes)
                {

                    ObjectTXT += System.Environment.NewLine + "Skybox Visual ID: " + skybox.objectID.ToString();
                    ObjectTXT += System.Environment.NewLine + "Skybox Strength: " + skybox.skyboxStrength.ToString();

                    ObjectTXT += System.Environment.NewLine + "Position: " + skybox.commonObjectData.entityProperties.position.X.ToString() + ", " + (-skybox.commonObjectData.entityProperties.position.Z).ToString() + ", " + skybox.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + skybox.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + skybox.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + skybox.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + skybox.commonObjectData.entityProperties.scale.X.ToString() + ", " + skybox.commonObjectData.entityProperties.scale.Z.ToString() + ", " + skybox.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine;
                }

                // Print the doors.
                if (room.layers[i].doorMP1s.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "DoorMP1s: ";
                }
                foreach (var door in room.layers[i].doorMP1s)
                {
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Door CHPR ID: " + door.chprId.ToString();
                    }
                    catch
                    {
                        ObjectTXT += System.Environment.NewLine + "Problem with getting Visual ID";
                    }

                    ObjectTXT += System.Environment.NewLine + "Position: " + door.commonObjectData.entityProperties.position.X.ToString() + ", " + (-door.commonObjectData.entityProperties.position.Z).ToString() + ", " + door.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + door.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + door.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + door.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + door.commonObjectData.entityProperties.scale.X.ToString() + ", " + door.commonObjectData.entityProperties.scale.Z.ToString() + ", " + door.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT = AppendAtlasLookup(ObjectTXT, room, door.commonObjectData);
                    ObjectTXT += System.Environment.NewLine;
                }

                // Print volume setups
                if (room.layers[i].volumetricFogs.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Volumetric Fogs: ";
                }
                foreach (var actor in room.layers[i].volumetricFogs)
                {
                    ObjectTXT += System.Environment.NewLine + "Position: " + actor.commonObjectData.entityProperties.position.X.ToString() + ", " + (-actor.commonObjectData.entityProperties.position.Z).ToString() + ", " + actor.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + actor.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + actor.commonObjectData.entityProperties.scale.X.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Z.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "instance index: " + actor.commonObjectData.originalInstanceData.id.ToString();

                    ObjectTXT += System.Environment.NewLine + "Color1: " + actor.color1.X.ToString() + ", " + actor.color1.Y.ToString() + ", " + actor.color1.Z.ToString() + ", " + actor.color1.W.ToString();
                    ObjectTXT += System.Environment.NewLine + "Color2: " + actor.color2.X.ToString() + ", " + actor.color2.Y.ToString() + ", " + actor.color2.Z.ToString() + ", " + actor.color2.W.ToString();

                    ObjectTXT += System.Environment.NewLine + "optionalUnk1: " + actor.optionalUnk1.ToString();
                    ObjectTXT += System.Environment.NewLine + "float1: " + actor.float1.ToString();
                    ObjectTXT += System.Environment.NewLine + "float2: " + actor.float2.ToString();
                    ObjectTXT += System.Environment.NewLine + "optionalUnk2: " + actor.optionalUnk2.ToString();
                    ObjectTXT += System.Environment.NewLine + "float3: " + actor.float3.ToString();
                    ObjectTXT += System.Environment.NewLine + "float4: " + actor.float4.ToString();
                    ObjectTXT += System.Environment.NewLine + "float5: " + actor.float5.ToString();
                    ObjectTXT += System.Environment.NewLine + "optionalUnk3: " + actor.optionalUnk3.ToString();
                    ObjectTXT += System.Environment.NewLine + "float6: " + actor.float6.ToString();
                    ObjectTXT += System.Environment.NewLine + "unknown bool: " + actor.unknownBool.ToString();

                    ObjectTXT += System.Environment.NewLine;
                }

                if (room.layers[i].volumetricFogRegions.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Volumetric Fog Regions: ";
                }
                foreach (var actor in room.layers[i].volumetricFogRegions)
                {
                    ObjectTXT += System.Environment.NewLine + "Position: " + actor.commonObjectData.entityProperties.position.X.ToString() + ", " + (-actor.commonObjectData.entityProperties.position.Z).ToString() + ", " + actor.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + actor.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + actor.commonObjectData.entityProperties.scale.X.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Z.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "instance data guid: " + actor.commonObjectData.originalInstanceData.id.ToString();

                    ObjectTXT += System.Environment.NewLine + "Far: " + actor.far.ToString();
                    ObjectTXT += System.Environment.NewLine + "Near: " + actor.near.ToString();
                    ObjectTXT += System.Environment.NewLine + "Color: " + actor.color.X.ToString() + ", " + actor.color.Y.ToString() + ", " + actor.color.Z.ToString() + ", " + actor.color.W.ToString();
                    ObjectTXT += System.Environment.NewLine + "Unk1: " + actor.unknown.ToString();

                    ObjectTXT += System.Environment.NewLine;
                }

                // Print water setups
                if (room.layers[i].waterRenderVolumes.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Water Render Volumes: ";
                }
                foreach (var actor in room.layers[i].waterRenderVolumes)
                {
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Water Model ID: " + actor.waterModel.ToString();
                    }
                    catch
                    {
                        ObjectTXT += System.Environment.NewLine + "Problem with getting Visual ID";
                    }

                    ObjectTXT += System.Environment.NewLine + "Water Normal Map: " + actor.waterNormalMap.ToString();
                    ObjectTXT += System.Environment.NewLine + "Water Color: " + actor.waterColor.X.ToString() + ", " + actor.waterColor.Y.ToString() + ", " + actor.waterColor.Z.ToString() + ", " + actor.waterColor.W.ToString();
                    ObjectTXT += System.Environment.NewLine + "Position: " + actor.commonObjectData.entityProperties.position.X.ToString() + ", " + (-actor.commonObjectData.entityProperties.position.Z).ToString() + ", " + actor.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + actor.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + actor.commonObjectData.entityProperties.scale.X.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Z.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "instance data guid: " + actor.commonObjectData.originalInstanceData.id.ToString();

                    ObjectTXT += System.Environment.NewLine;
                }

                if (room.layers[i].lavaRenderVolumes.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "Lava Render Volumes: ";
                }
                foreach (var actor in room.layers[i].lavaRenderVolumes)
                {
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Lava Model ID: " + actor.lavaModelId.ToString();
                    }
                    catch { }

                    ObjectTXT += System.Environment.NewLine + "Position: " + actor.commonObjectData.entityProperties.position.X.ToString() + ", " + (-actor.commonObjectData.entityProperties.position.Z).ToString() + ", " + actor.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + actor.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + actor.commonObjectData.entityProperties.scale.X.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Z.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Unk Vector: " + $"{actor.lavaVectorX}, {actor.lavaVectorY}, {actor.lavaVectorZ}, {actor.lavaVectorW}";
                    ObjectTXT += System.Environment.NewLine + "Unk Float 1: " + actor.unk1.ToString();
                    ObjectTXT += System.Environment.NewLine + "Unk Float 2: " + actor.unk2.ToString();
                    ObjectTXT += System.Environment.NewLine + "Unk Float 3: " + actor.unk3.ToString();
                    ObjectTXT += System.Environment.NewLine + "Unk Float 4: " + actor.unk4.ToString();

                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Optional Float 1: " + actor.optUnk1.ToString();
                    }
                    catch { }
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Optional Float 2: " + actor.optUnk2.ToString();
                    }
                    catch { }

                    ObjectTXT += System.Environment.NewLine;
                }

                // Print the actors.
                if (room.layers[i].actorMP1s.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "ActorMP1s: ";
                }
                foreach (var actor in room.layers[i].actorMP1s)
                {
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Actor Model ID: " + actor.staticModelId.ToString();
                    }
                    catch
                    {
                        ObjectTXT += System.Environment.NewLine + "Actor CHPR ID: " + actor.chprId.ToString();
                    }

                    ObjectTXT += System.Environment.NewLine + "Position: " + actor.commonObjectData.entityProperties.position.X.ToString() + ", " + (-actor.commonObjectData.entityProperties.position.Z).ToString() + ", " + actor.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + actor.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + actor.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + actor.commonObjectData.entityProperties.scale.X.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Z.ToString() + ", " + actor.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "instance data guid: " + actor.commonObjectData.originalInstanceData.id.ToString();
                    ObjectTXT = AppendAtlasLookup(ObjectTXT, room, actor.commonObjectData);
                    ObjectTXT += System.Environment.NewLine;
                }

                // Print the platforms.
                if (room.layers[i].platformMP1s.Count > 0)
                {
                    ObjectTXT += System.Environment.NewLine + System.Environment.NewLine + "PlatformMP1s: ";
                }
                foreach (var platform in room.layers[i].platformMP1s)
                {
                    try
                    {
                        ObjectTXT += System.Environment.NewLine + "Platform Visual ID: " + platform.staticModelId.ToString();
                    }
                    catch
                    {
                        ObjectTXT += System.Environment.NewLine + "Problem with getting Visual ID";
                    }

                    ObjectTXT += System.Environment.NewLine + "Position: " + platform.commonObjectData.entityProperties.position.X.ToString() + ", " + (-platform.commonObjectData.entityProperties.position.Z).ToString() + ", " + platform.commonObjectData.entityProperties.position.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "Rotation: " + platform.commonObjectData.entityProperties.blenderRotation.X.ToString() + ", " + platform.commonObjectData.entityProperties.blenderRotation.Y.ToString() + ", " + platform.commonObjectData.entityProperties.blenderRotation.Z.ToString();
                    ObjectTXT += System.Environment.NewLine + "Scale: " + platform.commonObjectData.entityProperties.scale.X.ToString() + ", " + platform.commonObjectData.entityProperties.scale.Z.ToString() + ", " + platform.commonObjectData.entityProperties.scale.Y.ToString();
                    ObjectTXT += System.Environment.NewLine + "instance data guid: " + platform.commonObjectData.originalInstanceData.id.ToString();
                    ObjectTXT = AppendAtlasLookup(ObjectTXT, room, platform.commonObjectData);
                    ObjectTXT += System.Environment.NewLine;
                }
            }

            File.WriteAllText(path + ".json", ObjectTXT);
            ObjectTXT = "Room Object Information: ";
        }

        private static string AppendAtlasLookup(string text, ConstructedRoom room, CommonObjectData objectData)
        {
            if (room == null ||
                objectData == null ||
                objectData.originalInstanceData == null ||
                room.lightMapIds == null ||
                room.lightMapAtlasLookups == null)
            {
                return text;
            }

            int count = Math.Min(
                room.lightMapIds.Count,
                room.lightMapAtlasLookups.Count);

            string objectId =
                objectData.originalInstanceData.id.ToString();

            for (int i = 0; i < count; i++)
            {
                if (room.lightMapIds[i].ToString() != objectId)
                    continue;

                SAtlasLookup lookup = room.lightMapAtlasLookups[i];

                text += System.Environment.NewLine +
                    "Atlas Lookup Index: " + i;

                text += System.Environment.NewLine +
                    "Atlas Offset U: " + lookup.offsetU.ToString();

                text += System.Environment.NewLine +
                    "Atlas Offset V: " + lookup.offsetV.ToString();

                text += System.Environment.NewLine +
                    "Atlas Scale: " + lookup.scale.ToString();

                text += System.Environment.NewLine +
                    "Atlas Unknown: " + lookup.unkD.ToString();

                text += System.Environment.NewLine +
                    "Lightmap UV: UV' = UV * " +
                    lookup.scale.ToString() +
                    " + (" +
                    lookup.offsetU.ToString() +
                    ", " +
                    lookup.offsetV.ToString() +
                    ")";

                return text;
            }

            return text;
        }
    }


}
