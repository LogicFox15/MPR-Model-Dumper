using IONET.Collada.Kinematics.Articulated_Systems;
using MetroidPrimeRemasterModelDumper.Tools;
using RetroStudioPlugin.Files.FileData;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DKCTF;
#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    public class ConstructedRoom
    {
        public List<ConstructedLayer> layers = new List<ConstructedLayer>();
        public CObjectId lightMapTxtr;
        public List<CObjectId> lightMapIds = new List<CObjectId>();
        public List<SAtlasLookup> lightMapAtlasLookups = new List<SAtlasLookup>();
        public ROOM originalRoomData;

        public static ConstructedRoom ProcessRoomForConstruction(ROOM room)
        {
            ConstructedRoom newRoom = new ConstructedRoom();
            newRoom.originalRoomData = room;

            if (room.HeadChunk.bakedLighting != null)
            {
                newRoom.lightMapTxtr =
                    room.HeadChunk.bakedLighting.lightMapTxtr;

                if (room.HeadChunk.bakedLighting.lightMapIds != null)
                {
                    newRoom.lightMapIds.AddRange(
                        room.HeadChunk.bakedLighting.lightMapIds);
                }

                if (room.HeadChunk.bakedLighting.atlasLookups != null)
                {
                    newRoom.lightMapAtlasLookups.AddRange(
                        room.HeadChunk.bakedLighting.atlasLookups);
                }
            }

            for (int i = 0; i < room.LayersChunk.layers.Count; i++)
            {
                ConstructedLayer constructedLayer = new ConstructedLayer();
                constructedLayer.name = room.LayersChunk.layers[i].layerHeader.name;

                for (int c = 0; c < room.LayersChunk.layers[i].srip.compChunk.components.Count; c++)
                {
                    var component = room.LayersChunk.layers[i].srip.compChunk.components[c];
                    var entity = room.ScriptData.entities[(int)component.propertyIndex];
                    var instance = room.ScriptData.InstanceData[(int)component.instanceIndex];

                    switch ((EGOComponentType)room.LayersChunk.layers[i].srip.compChunk.components[c].type)
                    {
                        case EGOComponentType.EntityProperties:
                            constructedLayer.entityProperties.Add(EntityProperties.Build(component, entity, instance));
                            break;
                        case EGOComponentType.RoomSettings:
                            Console.WriteLine("Found Room Settings");
                            constructedLayer.roomSettings.Add(RoomSettings.Build(component, entity, instance));
                            break;
                        case EGOComponentType.RoomController:
                            Console.WriteLine("Found Room Controller");
                            constructedLayer.roomControllers.Add(RoomController.Build(component, entity, instance));
                            break;
                        case EGOComponentType.BakedLightingPriorityModifierMP1:
                            Console.WriteLine("Found Baked Lighting Priority Modifier MP1");
                            constructedLayer.BakedLightingPriorityModifierMP1s.Add(BakedLightingPriorityModifierMP1.Build(component, entity, instance));
                            break;
                        case EGOComponentType.LightStatic:
                            Console.WriteLine("Found Light Static");
                            constructedLayer.lightStatics.Add(LightStatic.Build(component, entity, instance));
                            break;
                        case EGOComponentType.ColorModifier:
                            Console.WriteLine("Found Color Modifier");
                            constructedLayer.colorModifiers.Add(ColorModifier.Build(component, entity, instance));
                            break;
                        case EGOComponentType.ModCon:
                            Console.WriteLine("Found ModCon");
                            constructedLayer.modCons.Add(ModConScript.Build(component, entity, instance));
                            break;
                        case EGOComponentType.ActorMP1:
                            Console.WriteLine("Found ActorMP1");
                            constructedLayer.actorMP1s.Add(ActorMP1.Build(component, entity, instance));
                            break;
                        case EGOComponentType.PlatformMP1:
                            Console.WriteLine("Found PlatformMP1");
                            constructedLayer.platformMP1s.Add(PlatformMP1.Build(component, entity, instance));
                            break;
                        case EGOComponentType.DoorMP1:
                            Console.WriteLine("Found DoorMP1");
                            constructedLayer.doorMP1s.Add(DoorMP1.Build(component, entity, instance));
                            break;
                        case EGOComponentType.Skybox:
                            Console.WriteLine("Found Skybox");
                            constructedLayer.skyboxes.Add(Skybox.Build(component, entity, instance));
                            break;
                        case EGOComponentType.VolumetricFog:
                            Console.WriteLine("Found VolumetricFog");
                            constructedLayer.volumetricFogs.Add(VolumetricFog.Build(component, entity, instance));
                            break;
                        case EGOComponentType.VolumetricFogRegion:
                            Console.WriteLine("Found VolumetricFogRegion");
                            constructedLayer.volumetricFogRegions.Add(VolumetricFogRegion.Build(component, entity, instance));
                            break;
                        case EGOComponentType.WaterRenderVolume:
                            Console.WriteLine("Found WaterRenderVolume");
                            constructedLayer.waterRenderVolumes.Add(WaterRenderVolume.Build(component, entity, instance));
                            break;
                        case EGOComponentType.LavaRenderVolume:
                            Console.WriteLine("Found LavaRenderVolume");
                            constructedLayer.lavaRenderVolumes.Add(LavaRenderVolume.Build(component, entity, instance));
                            break;
                    }
                }

                PrepareTransforms(constructedLayer, room);
                newRoom.layers.Add(constructedLayer);
            }

            return newRoom;


            // Clear before processing another room
            // layers = new List<ConstructedLayer>();
        }

        public static void PrepareTransforms(ConstructedLayer parsed, ROOM room)
        {
            foreach (var retroObject in parsed.roomControllers)
            {
                RoomController.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.lightStatics)
            {
                LightStatic.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.modCons)
            {
                ModConScript.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.skyboxes)
            {
                Skybox.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.volumetricFogs)
            {
                VolumetricFog.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.volumetricFogRegions)
            {
                VolumetricFogRegion.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.waterRenderVolumes)
            {
                WaterRenderVolume.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.lavaRenderVolumes)
            {
                LavaRenderVolume.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.actorMP1s)
            {
                ActorMP1.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.doorMP1s)
            {
                DoorMP1.prepTransform(retroObject, parsed);
            }
            foreach (var retroObject in parsed.platformMP1s)
            {
                PlatformMP1.prepTransform(retroObject, parsed);
            }
        }
    }

    public class ConstructedLayer
    {
        public string name;
        public List<EntityProperties> entityProperties = new List<EntityProperties>();
        public List<RoomSettings> roomSettings = new List<RoomSettings>();
        public List<RoomController> roomControllers = new List<RoomController>();
        public List<BakedLightingPriorityModifierMP1> BakedLightingPriorityModifierMP1s = new List<BakedLightingPriorityModifierMP1>();
        public List<LightStatic> lightStatics = new List<LightStatic>();
        public List<ColorModifier> colorModifiers = new List<ColorModifier>();
        public List<ModConScript> modCons = new List<ModConScript>();
        public List<ActorMP1> actorMP1s = new List<ActorMP1>();
        public List<PlatformMP1> platformMP1s = new List<PlatformMP1>();
        public List<DoorMP1> doorMP1s = new List<DoorMP1>();
        public List<Skybox> skyboxes = new List<Skybox>();
        public List<VolumetricFog> volumetricFogs = new List<VolumetricFog>();
        public List<VolumetricFogRegion> volumetricFogRegions = new List<VolumetricFogRegion>();
        public List<WaterRenderVolume> waterRenderVolumes = new List<WaterRenderVolume>();
        public List<LavaRenderVolume> lavaRenderVolumes = new List<LavaRenderVolume>();
    }

    public class ModularConstructionContainer
    {
        public List<CMDL> models = new List<CMDL>();
    }

}
