using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;


namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class RoomController
    {
        public CommonObjectData commonObjectData = new CommonObjectData();
        public CObjectId roomId;

        public static RoomController Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            RoomController script = new RoomController();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildRoomControllerProperties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static RoomController prepTransform(RoomController retroObject, ConstructedLayer parsed)
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

        public static void BuildRoomControllerProperties(FileReader reader, RoomController script)
        {
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                ReadRoomControllerProperties(reader, script);
            }
        }

        public static void ReadRoomControllerProperties(FileReader reader, RoomController script)
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
                case 0x30a4d63d:    // Nested CHPR container
                    script.roomId = propertyReader.ReadStruct<CObjectId>();
                    break;
                default:
                    break;
            }
        }

        public static string GetNameFromId(string id)
        {
            string name = "Unknown ID";
            string newID = id.ToString();

            switch (newID.ToLower())
            {
                case "813189fe-63d2-4f96-a9f4-4fb461d07f99": name = "UI_HUDMPR1"; break; //[cite: 1]
                case "69c4c4f5-271c-433b-a5ac-54d9484af3c4": name = "KernelMPRT"; break; //[cite: 1]
                case "8397a55d-605d-4200-a4ff-fa05c45efb44": name = "UI_FrontEnd"; break; //[cite: 1]
                case "1f8ff237-e661-4bcc-add4-d8f740c22d18": name = "35-MetroidPrime2"; break; //[cite: 1]
                case "9639a6a8-f5a9-4a4f-aa2a-aac9d5ca4498": name = "!DefaultRoom"; break; //[cite: 1]
                case "29ea4443-3404-40e5-ab88-c892cd122701": name = "00-Ship"; break; //[cite: 1]
                case "e76b3f23-83fc-41ac-bdb9-ad173c054369": name = "01-PowerSuit"; break; //[cite: 1]
                case "f573b24c-11e3-4c97-a54a-9242529c1ecc": name = "02-VariaSuit"; break; //[cite: 1]
                case "899fce7c-16d8-4464-bc5b-147199dd121e": name = "03-GravitySuit"; break; //[cite: 1]
                case "7df0f71b-e0a2-45f3-a974-75f1d517e25a": name = "04-PhazonSuit"; break; //[cite: 1]
                case "6f8074ca-c6ea-422d-957a-e240f900a20c": name = "05-MorphBall"; break; //[cite: 1]
                case "801d983e-72f3-4695-9e5e-c3b1e62c67f0": name = "06-SpiderBall"; break; //[cite: 1]
                case "5dd78edc-85f4-4991-91e1-b97b70d0e5f3": name = "07-AutoDefenseTurret"; break; //[cite: 1]
                case "93609836-523e-4747-b60c-3173749ce513": name = "08-InjuredPirates"; break; //[cite: 1]
                case "b370d358-04ba-49cc-bc96-61e4b51a7e14": name = "09-Parasites"; break; //[cite: 1]
                case "0e5a33c0-63f4-47c3-b90a-0a5ee539cdc5": name = "10-ParasiteQueen"; break; //[cite: 1]
                case "2411a42c-9ef1-40e3-9d37-9d6f1068704f": name = "11-Beetle"; break; //[cite: 1]
                case "0f5cc75d-2f4a-4f02-ba12-26e1cafbd175": name = "12-WarWasp"; break; //[cite: 1]
                case "6850f1d2-a459-4ed0-9e43-f4b4267dd369": name = "13-IncineratorDrone"; break; //[cite: 1]
                case "a0b75edd-83eb-4ad5-893e-5fb0f273e893": name = "14-HiveMecha"; break; //[cite: 1]
                case "860d554b-8287-4deb-a406-0036df764722": name = "15-PlatedBeetle"; break; //[cite: 1]
                case "43801f23-5529-4b4d-b468-edc3da51b65f": name = "16-Flaahgra"; break; //[cite: 1]
                case "96f560fc-762f-4647-8e60-6748f944b7c5": name = "17-Geemer"; break; //[cite: 1]
                case "25b7275d-ed80-4bbe-8431-30a021ff5a7a": name = "18-ChozoGhost"; break; //[cite: 1]
                case "cc684e87-20d2-4c14-9e4d-aa3e8b099144": name = "19-BabySheegoth"; break; //[cite: 1]
                case "7ba1e5d4-8d3e-4933-bcc7-e6ee20d17d02": name = "20-Sheegoth"; break; //[cite: 1]
                case "74a23c5a-8e51-4b14-afc7-5090c4bb7dea": name = "21-Thardus"; break; //[cite: 1]
                case "7a8a3eb5-3b2d-4b00-92bb-328d0a8f0762": name = "22-MegaTurret"; break; //[cite: 1]
                case "77b60e4a-29c0-448a-94a4-6bceffe3e9a3": name = "23-SpacePirate"; break; //[cite: 1]
                case "9f999f69-c0b1-491b-85f8-982a3dea0843": name = "24-TrooperPirate"; break; //[cite: 1]
                case "85ed5d2d-375e-4e29-80ec-4055ded6b1a3": name = "25-ElitePirate"; break; //[cite: 1]
                case "8023ebcd-06a4-4f3a-9f0e-fbe3e3f6c7ad": name = "26-OmegaPirate"; break; //[cite: 1]
                case "b570da4a-f9b9-48ae-991e-0c629d351f7d": name = "27-ShadowPirate"; break; //[cite: 1]
                case "975d1483-78cd-44d1-91a9-f7f6b2fc94a3": name = "28-FlyingPirate"; break; //[cite: 1]
                case "c7afe6ca-4262-4b2d-a7b1-cc397d783b25": name = "29-AquaPirate"; break; //[cite: 1]
                case "278af6b8-6f5e-43ac-8892-fd6f7d5c114b": name = "30-PhazonElite"; break; //[cite: 1]
                case "4d1dd516-9685-4d87-950e-550245f3dc5a": name = "31-MetaRidley"; break; //[cite: 1]
                case "9b369fb3-a6ba-4163-92c4-71a84bc718cc": name = "32-Metroids"; break; //[cite: 1]
                case "79254505-06b9-4ddd-9843-563f44dff06e": name = "33-HunterMetroid"; break; //[cite: 1]
                case "752543ac-0325-48a0-9b71-fc1e9d8ad52b": name = "34-MetroidPrime1"; break; //[cite: 1]
                case "4998ebe1-4656-423a-8e69-809bcd492382": name = "defaultWorldElevator"; break; //[cite: 1]
                case "1230b718-876e-4c45-bbfc-2ae37913b64a": name = "universeRoomMP1"; break; //[cite: 1]
                case "73318837-09ac-4dbb-bc66-d26950259b27": name = "!Crater_Master"; break; //[cite: 1]
                case "6929c583-d47a-407c-8014-586c7769395c": name = "00_crater_over_elev_J"; break; //[cite: 1]
                case "5a96389f-c158-4ff3-b43b-b78817d8863f": name = "00a_Crater_connect"; break; //[cite: 1]
                case "265e3c78-3321-4dfd-a3bd-636419faa742": name = "00b_Crater_connect"; break; //[cite: 1]
                case "ffdf2c67-ca22-47ae-b6ef-77fe49e36d5f": name = "01_Crater_dental"; break; //[cite: 1]
                case "4f0d441c-e8f6-43ad-9d2b-0f1bb4ba903c": name = "03a_crater"; break; //[cite: 1]
                case "6ed2b08f-9e63-4220-8dc2-72bc75f0894d": name = "03b_Crater"; break; //[cite: 1]
                case "79cba2e8-3da2-4cdd-8bcd-ff4976807421": name = "03c_Crater"; break; //[cite: 1]
                case "e655de83-a6ea-4f23-a13b-731b8c626d9e": name = "03d_crater"; break; //[cite: 1]
                case "6a6653a2-0d94-42ca-8822-3af1e402a134": name = "03e_Crater"; break; //[cite: 1]
                case "43b3c4f8-2fa6-44b4-88d4-4b1e28be3141": name = "03E_F_Crater"; break; //[cite: 1]
                case "a103fb3c-0be0-428b-83c6-c69fa31b883f": name = "03f_Crater"; break; //[cite: 1]
                case "d2879a71-a63e-48e1-ab95-95b289d55498": name = "MissileRechargeStation_Crater"; break; //[cite: 1]
                case "2488bc40-757c-47c4-91fd-c6ca6232e888": name = "Savestation_Ice_C"; break; //[cite: 1]
                case "81b62adb-88f3-46f0-a43c-b8ccdfc354df": name = "!IceWorld"; break; //[cite: 1]
                case "b035c80a-afbf-4ed5-b5e9-592c5a7c0ff6": name = "00_ice_elev_lava_C"; break; //[cite: 1]
                case "1686a3ef-f84d-4195-a989-cee3078817a5": name = "00_ice_elev_lava_D"; break; //[cite: 1]
                case "bd20f0b5-1d2d-4f39-aa07-dca694169277": name = "00b_ice_connect"; break; //[cite: 1]
                case "489372c0-5e86-411f-a9ce-43eb4aec9822": name = "00d_ice_connect"; break; //[cite: 1]
                case "5731ab95-0d5e-412e-be85-b8692bc87738": name = "00e_ice_connect"; break; //[cite: 1]
                case "25517759-e7d3-4ceb-be3e-9da3930bdf1f": name = "00f_ice_connect"; break; //[cite: 1]
                case "c2cddbda-7387-426d-8130-593c4c5754ce": name = "00g_ice_connect"; break; //[cite: 1]
                case "fd68839b-cb4a-4170-b981-645bd72d8f25": name = "00h_ice_connect"; break; //[cite: 1]
                case "4b37c77e-f43e-427c-af85-ba430ecd494a": name = "00i_ice_connect"; break; //[cite: 1]
                case "fa42b9bf-1889-4d05-b60d-e7ce72001d8f": name = "00j_ice_connect"; break; //[cite: 1]
                case "15826ed6-d3eb-4bf2-9069-5013b2000b1c": name = "00k_ice_connect"; break; //[cite: 1]
                case "7ba12346-38c1-41fc-be49-d3207829dec2": name = "00L_connect"; break; //[cite: 1]
                case "70b243e8-b15f-4dd7-a992-0f683472109c": name = "00n_ice_connect"; break; //[cite: 1]
                case "edd535be-da99-4d3e-89f4-a1119dcac52a": name = "00o_ice_connect"; break; //[cite: 1]
                case "020db159-835e-4220-9539-099872220225": name = "00p_ice_connect"; break; //[cite: 1]
                case "5e4a526e-1ddd-49e6-ab0c-45caed80e981": name = "00q_ice_connect"; break; //[cite: 1]
                case "1c4a4e9f-164e-465c-9b64-202f68b0b0a6": name = "00r_ice_connect"; break; //[cite: 1]
                case "179b8af7-bd1a-4557-ab12-646c53ba82bc": name = "00s_ice_connect"; break; //[cite: 1]
                case "d4cffa37-1b34-454b-b22e-6fbedc9721c6": name = "0_common_elevator_A"; break; //[cite: 1]
                case "7713a4a2-47a9-467f-95fa-4d714fbd0e0c": name = "0_common_elevator_B"; break; //[cite: 1]
                case "7b341258-6730-4f67-9921-b57acbc9bb39": name = "01_Ice_Plaza"; break; //[cite: 1]
                case "cd2f9bd1-8c8f-4085-a53b-28c5496db21e": name = "02_Ice_Ruins_A"; break; //[cite: 1]
                case "2947357d-79b5-4d7b-acbc-a671d671bda9": name = "03_Ice_Ruins_B"; break; //[cite: 1]
                case "d22f1fcf-afb1-4442-ac4d-138201ecf774": name = "04_Ice_Boost_canyon"; break; //[cite: 1]
                case "22c5fe58-3eb1-43cf-880c-5cd95db58c79": name = "05_Ice_Shorelines"; break; //[cite: 1]
                case "b0414f85-807b-4269-bace-6baf81a6ba7a": name = "06_Ice_Chapel"; break; //[cite: 1]
                case "eddf2de3-ec6c-49e7-a9a4-f43e1669ee2a": name = "06_Ice_Temple"; break; //[cite: 1]
                case "ff16bd31-ca8e-4d03-9004-b09fdd3be45a": name = "08_Ice_Ridley"; break; //[cite: 1]
                case "263c9808-1ede-4fda-b9c3-c3b37477b51d": name = "08_Ice_Ridley_Broken_Tower_Copy"; break; //[cite: 1]
                case "51cc658c-83be-40bd-9a73-4e982dfe054b": name = "09_Ice_Lobby"; break; //[cite: 1]
                case "28d355d0-bee9-4e7e-b5f4-bd873de4c17e": name = "10_Ice_Research_A"; break; //[cite: 1]
                case "a750163a-5c4d-495e-b4c4-c43a5d084670": name = "11_Ice_Observatory"; break; //[cite: 1]
                case "97f17596-feea-434e-b637-e5a6f08354e9": name = "12_Ice_Research_B"; break; //[cite: 1]
                case "1216cf7c-7d35-40c7-8f07-04109792b153": name = "13_Ice_Vault"; break; //[cite: 1]
                case "d424227b-1365-441d-a5bd-47258995c5d6": name = "14_Ice_Tower_A"; break; //[cite: 1]
                case "d31b22bf-25ee-4c24-be56-14f9753f8bbc": name = "15_Ice_Cave_A"; break; //[cite: 1]
                case "07b054ae-5c56-433f-9ff5-1e21782f737e": name = "16_Ice_Tower_B"; break; //[cite: 1]
                case "aa9dc12a-5e2a-4ddc-88d0-ea5fbcac5267": name = "17_Ice_Cave_B"; break; //[cite: 1]
                case "068146e1-27a7-4730-aaa3-e9beff7bea99": name = "18_Ice_Gravity_Chamber"; break; //[cite: 1]
                case "169aaa49-a6f2-4160-a25f-34072b83b1cb": name = "19_Ice_Thardus"; break; //[cite: 1]
                case "f96b5c69-cd04-4a81-8741-f59b140eba06": name = "generic_x1"; break; //[cite: 1]
                case "c3d790fd-c9e2-467a-a597-3c6ebc584f35": name = "generic_z1"; break; //[cite: 1]
                case "9a09985e-dee8-4a4d-b343-e41648108a18": name = "generic_z2"; break; //[cite: 1]
                case "a25b2990-8904-44c2-9beb-acfd5cf45343": name = "generic_z3"; break; //[cite: 1]
                case "f4833a33-e01f-4ac9-bbe2-809f212ef13c": name = "generic_z4"; break; //[cite: 1]
                case "61c6ba21-ab09-4a0c-a9b1-460e39a292fe": name = "generic_z5"; break; //[cite: 1]
                case "fb6edf68-5e12-451c-ae5c-bbea968b6ef7": name = "generic_z6"; break; //[cite: 1]
                case "68ed055c-3df8-4569-9a73-e972f3674115": name = "generic_z7"; break; //[cite: 1]
                case "0b342d63-0703-4856-84c3-7b9d37624f42": name = "generic_z8"; break; //[cite: 1]
                case "041e2ed6-68ac-4e1a-b048-1e905214e69c": name = "Mapstation_Ice"; break; //[cite: 1]
                case "2d208919-7e38-4c70-bf31-006a0c796ddd": name = "pickup01"; break; //[cite: 1]
                case "d86989fc-0518-4606-9229-46d07b51f8ca": name = "pickup02"; break; //[cite: 1]
                case "19dd0644-1c6d-453b-89a5-57c456389b94": name = "pickup03"; break; //[cite: 1]
                case "2b8d8aa2-2e26-4c48-a60c-8026e66ef9fe": name = "pickup04"; break; //[cite: 1]
                case "bfc61917-03bd-44ef-b770-200241e4f3c5": name = "Savestation_Ice_A"; break; //[cite: 1]
                case "18da0c1f-0e12-4565-9ff1-a35de24b099e": name = "Savestation_Ice_B"; break; //[cite: 1]
                case "dff94134-9682-471d-84c3-f640e3325f7b": name = "09_Intro_Ridley_Chamber"; break; //[cite: 1]
                case "4b5e8635-0f43-4ffb-9427-2e62186e517d": name = "!Intro_Master"; break; //[cite: 1]
                case "acf030f7-f21a-49a5-b406-f61fb3c99096": name = "00a_Intro_Connect"; break; //[cite: 1]
                case "5ddd9242-b400-4a0b-9500-01f6aaa75812": name = "00b_Intro_Connect"; break; //[cite: 1]
                case "238ddffb-fa8f-45a4-9725-6c405717f006": name = "00c_Intro_Connect"; break; //[cite: 1]
                case "73849ee0-b1dc-4341-9af2-472a741b6420": name = "00d_Intro_connect"; break; //[cite: 1]
                case "bb849b7d-acce-4fc8-8c6c-fb50b67da636": name = "00e_Intro_connect"; break; //[cite: 1]
                case "9bc16ed5-3de4-4d71-ae86-ebd20d73a9de": name = "00F_intro_begin"; break; //[cite: 1]
                case "649c1c74-6868-497d-9897-68d67d654c02": name = "00F_intro_end"; break; //[cite: 1]
                case "c9661447-d18f-47ce-a708-2c7a1b534fff": name = "00g_intro_begin"; break; //[cite: 1]
                case "b77a5420-d033-479b-8b3c-a9d1448691dc": name = "00g_intro_end"; break; //[cite: 1]
                case "e85b53bf-3ee1-40d2-b53a-661f7671e22c": name = "00h_Intro_mechshaft"; break; //[cite: 1]
                case "a8444ff6-e4a4-4183-bfec-73d6d6407fa5": name = "01_intro_hanger"; break; //[cite: 1]
                case "bc4909ee-a059-47bc-a5bf-aacaa5b76f42": name = "01_intro_hanger_connect"; break; //[cite: 1]
                case "7e86216e-c5b8-4c29-bad9-bf07e894e771": name = "02_Intro_Elevator"; break; //[cite: 1]
                case "068b0898-1301-4940-a45e-50eaffc9a2ca": name = "02_Intro_Epod_connect"; break; //[cite: 1]
                case "4ca69978-a46a-4aa4-bedc-3fc916f1f363": name = "02_Intro_Epodroom"; break; //[cite: 1]
                case "0eb1626b-51f6-4ccd-8bf9-163259ae2b5b": name = "03_Intro_Elevator"; break; //[cite: 1]
                case "056ba4ba-ac3a-4a78-8834-89717c5b479b": name = "04_Intro_Specimen_Chamber"; break; //[cite: 1]
                case "ead09284-6e8b-424d-a640-a3c8e46c5797": name = "05_Zoo"; break; //[cite: 1]
                case "f24d07ea-3156-419f-ab6a-ef8b9818b785": name = "06_Intro_Freight_Lifts"; break; //[cite: 1]
                case "a1b4e4b4-6167-4f59-8fe0-81e28c57c57a": name = "06_Intro_To_Reactor"; break; //[cite: 1]
                case "b4c87df0-28db-47bd-9b40-0314cff6f41c": name = "07_Intro_Reactor"; break; //[cite: 1]
                case "a842fa80-5949-4032-89ed-26c4cc06019d": name = "08a_Intro_ventshaft"; break; //[cite: 1]
                case "69c2b6b5-00f7-4ecc-9739-e779ff4134d8": name = "08b_Intro_ventshaft"; break; //[cite: 1]
                case "e0a4783d-872a-4ed4-9745-55c12d6fa544": name = "08c_Intro_ventshaft"; break; //[cite: 1]
                case "64ddc2e1-905f-4c8f-8b19-673c3700fe3b": name = "08d_Intro_ventshaft"; break; //[cite: 1]
                case "08a88b1c-ba6a-42aa-8621-7d507dd1ba20": name = "08e_Intro_ventshaft"; break; //[cite: 1]
                case "3a60e427-308e-4748-9383-6852cb10f8d1": name = "08f_Intro_ventshaft"; break; //[cite: 1]
                case "3f0b988e-fe61-4dab-bf39-45ca2fd33e16": name = "B_Lava_Savestation"; break; //[cite: 1]
                case "f5712eed-dde1-47bf-8670-d0664954861a": name = "!Lava_Master"; break; //[cite: 1]
                case "38a2b289-d65e-4c05-a8f9-82ecb8e5e511": name = "00_lava_elev_ice_C"; break; //[cite: 1]
                case "ac0cf9d6-744f-4a6b-a90a-070c002462a1": name = "00_Lava_Elev_Ice_D"; break; //[cite: 1]
                case "eb1b3a53-e103-4047-90dc-00dcb753f34d": name = "00_lava_elev_over_L"; break; //[cite: 1]
                case "c6075efb-d0e2-46af-956b-cb7c1e2d28c8": name = "00_Lava_elev_Ruins_B"; break; //[cite: 1]
                case "617c984a-7a0e-4816-abfe-f2a0798ade61": name = "00_Lava_Mines_Elev_H"; break; //[cite: 1]
                case "bd970041-5dfe-4f5e-ba1b-eba04492d860": name = "00a_lava_connect"; break; //[cite: 1]
                case "076a3d1e-e15a-4834-b092-c5d0cc9de677": name = "00b_Lava_Connect"; break; //[cite: 1]
                case "ea5248b7-292c-48dd-a166-bc6b029505d4": name = "00c_lava_connect"; break; //[cite: 1]
                case "11772058-6b13-4b5f-97dd-16ca6a1eb5c5": name = "00d_lava_connect"; break; //[cite: 1]
                case "45297d92-9506-43e0-80ff-76421b5b6353": name = "00e_lava_connect"; break; //[cite: 1]
                case "9fa37516-547d-4f7b-9a54-0249fdf8e705": name = "00f_lava_connect"; break; //[cite: 1]
                case "58d0e11c-8456-4401-95c9-9479a7b92bfe": name = "00g_Lava_Connect"; break; //[cite: 1]
                case "16ea7e97-ff5a-4e4e-b118-91a72f6fd2fe": name = "00H_Lava_connect"; break; //[cite: 1]
                case "774eaac9-22dd-41d2-81c4-b8cb3f91766e": name = "00i_Lava_Connect"; break; //[cite: 1]
                case "81fcbab4-c610-47ee-8cb1-ab21d539d2f6": name = "00j_Lava_connect"; break; //[cite: 1]
                case "61c700d9-426f-417b-9e0e-846b3a2093d2": name = "00k_Lava_Connect"; break; //[cite: 1]
                case "0b103df5-bf6a-45a7-a5fd-2579bf1becb3": name = "08_Over_muddywaters_A"; break; //[cite: 1]
                case "c3024d04-7760-4927-a033-c02371d4911e": name = "09_Lava_pickup"; break; //[cite: 1]
                case "eea18a28-c10c-45e3-b3df-20137da7b8bd": name = "09_Over_monitortower"; break; //[cite: 1]
                case "57afa03d-85f9-4c39-99e6-2d9eebaa4939": name = "10_Over_1Alavaarea"; break; //[cite: 1]
                case "301d9dc0-8d85-41e5-8ad0-f389fad09d4f": name = "11_Over_muddywaters_B"; break; //[cite: 1]
                case "5898a308-f7ee-4eab-a94e-661d4042f800": name = "12_Over_fieryshores"; break; //[cite: 1]
                case "65f989f3-1dd2-4cec-b379-75244367ee33": name = "13_Lava_Pickup"; break; //[cite: 1]
                case "5bb50029-3789-43fa-ac80-1e3e0133a992": name = "13_Over_burningeffigy"; break; //[cite: 1]
                case "75d31737-205e-495e-b8c7-e61b0e66620b": name = "14_lava_pickup"; break; //[cite: 1]
                case "baba5edd-a580-4c89-979a-be1a9442bad2": name = "14_Over_magdolitepits"; break; //[cite: 1]
                case "697ea4e1-be7e-42e8-91f8-2e7033b24cbb": name = "23_lava_BurningTrail"; break; //[cite: 1]
                case "44345d54-1765-4e50-ad7c-19bbd35afddf": name = "A_Lava_SaveStation"; break; //[cite: 1]
                case "28af1864-6ced-4568-bb8a-bfe72a328430": name = "13_mines_vertical_ascent"; break; //[cite: 1]
                case "d3e02263-def7-4a66-bef4-26a45e8c59a8": name = "!Mines_Master"; break; //[cite: 1]
                case "27dd7619-bf41-487d-9086-de942c66d632": name = "00_Mine_lava_elev_G"; break; //[cite: 1]
                case "47e79ddd-b62f-455f-9d0d-56a69f2a8555": name = "00_mines_lava_elev_H"; break; //[cite: 1]
                case "18762b1d-e730-4445-bd3c-bbbbcc7a0d3c": name = "00_Mines_Mapstation"; break; //[cite: 1]
                case "b0639e71-42c5-4b4e-ad86-0ab7e9dd7463": name = "00_Mines_pickup_02"; break; //[cite: 1]
                case "059b2cbf-27f0-4b95-83e1-19c6b6d13285": name = "00_Mines_pickup_04"; break; //[cite: 1]
                case "27231238-a135-49ae-91a9-c856fd30d85c": name = "00_Mines_Savestation_A"; break; //[cite: 1]
                case "2bfd1682-9a04-461b-92ef-f58c8bc6a882": name = "00_Mines_Savestation_B"; break; //[cite: 1]
                case "86b6c7ad-1a75-4368-9c5a-d81c57038895": name = "00_Mines_Savestation_C"; break; //[cite: 1]
                case "de238862-af58-4713-9f94-35a244f8861f": name = "00_Mines_Savestation_D"; break; //[cite: 1]
                case "cafb1470-7926-4a1f-99bf-4bd5b2535ead": name = "00a_Mines_connect"; break; //[cite: 1]
                case "d5b15031-4e92-4652-b892-676fd73f85e5": name = "00b_mines_connect"; break; //[cite: 1]
                case "6991e9cf-9eba-4e6b-b9bb-5d2e6de54b2c": name = "00c_Mines_connect"; break; //[cite: 1]
                case "ba29e428-61d2-4ec9-bbe0-08cba5407acb": name = "00d_Mines_Connect"; break; //[cite: 1]
                case "ce776ee9-2beb-4227-8593-bd0d281ddd66": name = "00e_Mines_Connect"; break; //[cite: 1]
                case "a5e8ebbc-31d9-454b-8665-2424275aa239": name = "00f_Mines_connect"; break; //[cite: 1]
                case "569f2e33-aca1-415a-ba18-4e4d245dde06": name = "00g_Mines_connect"; break; //[cite: 1]
                case "7ed58f1b-4537-40c9-a8c6-b4c8aaa854c9": name = "00h_mines_connect"; break; //[cite: 1]
                case "e3d45dbb-a6f5-42de-9320-6e61991bd6b2": name = "00i_Mines_connect"; break; //[cite: 1]
                case "5f46b758-a6d7-45eb-b517-18baf91667ed": name = "00j_Mines_connect"; break; //[cite: 1]
                case "46508bfc-6dd2-494f-a1e8-29b44d912645": name = "00k_Mines_connect"; break; //[cite: 1]
                case "044a2f2f-3a80-418b-bcdc-a46f86f886f6": name = "00l_Mines_connect"; break; //[cite: 1]
                case "78586827-e5fa-4900-ba48-74af699fbbd3": name = "00m_mines_connect"; break; //[cite: 1]
                case "76fe4520-18c0-4065-94b6-8678ce1cc027": name = "00N_Mines_connect"; break; //[cite: 1]
                case "c1f4d36b-9ae2-4da6-888a-737113e4c3d2": name = "00o_mines_connect"; break; //[cite: 1]
                case "a1301d54-e3a5-4f36-8c8f-ef688b75e625": name = "00p_Mines_Connect"; break; //[cite: 1]
                case "2a2250c8-f794-41d0-8244-cf125180cb25": name = "00r_Mines_connect"; break; //[cite: 1]
                case "e8e437ba-a661-44fd-8903-8c73470ee2b8": name = "00s_Mines_connect"; break; //[cite: 1]
                case "ca28b4a7-cfc8-4e69-9491-7d04f203e6c3": name = "01_Mines_MainPlaza"; break; //[cite: 1]
                case "dd9802c7-bc65-4259-a1ac-e7ff9ebb9fc2": name = "1_2_Mines_Elevator"; break; //[cite: 1]
                case "52c9f613-fd60-4313-83da-85e818fe3690": name = "02_Mines_shootemup"; break; //[cite: 1]
                case "003668d1-559e-4569-bad8-cabcab36969b": name = "2_3_Mines_Elevator"; break; //[cite: 1]
                case "601aac0f-e7a4-4095-96f9-7cea4b39a20b": name = "03_mines"; break; //[cite: 1]
                case "eada72d1-a1d5-4ede-a4cf-a920d6f3323c": name = "04_Mines_pillar"; break; //[cite: 1]
                case "16094b40-d1bf-4998-b111-f582dcc228ba": name = "05_Mines_forcefields"; break; //[cite: 1]
                case "fad3dc94-47ec-46c2-9634-c66d131b798e": name = "06_Mines_elitebustout"; break; //[cite: 1]
                case "2a5c08f3-4937-4456-8aac-bbf94287f0c7": name = "07_Mines_electric"; break; //[cite: 1]
                case "47aeada4-bfe4-489b-8383-93a116b1c684": name = "08_Mines"; break; //[cite: 1]
                case "b430246c-1b04-419e-8261-ec908f8aab2d": name = "09_Mines_mushroomhall"; break; //[cite: 1]
                case "26959a74-099f-4d80-80d8-23dc474e7a3e": name = "10_Mines_altmushroomhall"; break; //[cite: 1]
                case "1a616ba1-f89f-4b01-b6db-9eb335b450ea": name = "11_mines"; break; //[cite: 1]
                case "d216e17a-742c-4fd3-a350-9114b9ba4ba0": name = "12_Mines_eliteboss"; break; //[cite: 1]
                case "d8887084-5878-4e2e-89d2-191d0117b227": name = "08c_IntroUnderwater_ventshaft"; break; //[cite: 1]
                case "73f2640c-cfb2-407b-a786-47a263ef0001": name = "!Over_Master"; break; //[cite: 1]
                case "fa869fe6-c689-4461-bea7-7d87364abb10": name = "00_over_elev_lava_L"; break; //[cite: 1]
                case "54be5fd1-916a-4523-82a6-72d335c868a5": name = "00_over_elev_mines_G"; break; //[cite: 1]
                case "863591c2-2ebb-47d5-9650-c033f715ddf2": name = "00_over_elev_ruins_A"; break; //[cite: 1]
                case "8e47d4ba-2c40-4389-804a-8b041dae8062": name = "00_over_elev_ruins_E"; break; //[cite: 1]
                case "e3f5266d-4404-41bc-8281-05e002c86925": name = "00_over_elev_ruins_F"; break; //[cite: 1]
                case "80b7af17-9ed6-4008-85b5-6d1870067be2": name = "00a_IntroUnderwater_Connect"; break; //[cite: 1]
                case "eebfd30b-e35a-4538-8fd2-5dd0da79b27a": name = "00a_over_hall"; break; //[cite: 1]
                case "94aea917-ce72-406f-9926-36f92dd54d42": name = "00b_IntroUnderwater_connect"; break; //[cite: 1]
                case "74850a80-959c-44c6-817d-4686fcd91b7c": name = "00b_over_hall"; break; //[cite: 1]
                case "684f3542-8105-466f-b4d3-1219cb4fee8e": name = "00c_IntroUnderwater_Connect"; break; //[cite: 1]
                case "5c7d3ae1-3065-4797-a970-50e6b6d216d9": name = "00d_IntroUnderwater_connect"; break; //[cite: 1]
                case "585aecfb-62c1-422e-81f0-e342a6cd2a19": name = "00d_over_hall"; break; //[cite: 1]
                case "672aab7f-c8b0-4a14-bfc3-28fa89bd723a": name = "00e_IntroUnderwater_connect"; break; //[cite: 1]
                case "34ac2610-bc39-4af9-b8a0-9412e736bf10": name = "00f_over_hall"; break; //[cite: 1]
                case "cd563322-dae5-4693-a7d6-d56f333f4d34": name = "00g_over_hall"; break; //[cite: 1]
                case "ae1e094b-2e4d-4e35-bf0d-3f8fc3c4a958": name = "00j_over_hall"; break; //[cite: 1]
                case "5118d8cc-7d03-49a7-8ce6-627db03d31dd": name = "00j_over_plaza_hall"; break; //[cite: 1]
                case "2103a083-15f5-4cee-98f0-f83d204b0f1e": name = "00k_over_hall"; break; //[cite: 1]
                case "c4244411-63ed-4c68-b0df-86273693ad94": name = "00l_over_hall"; break; //[cite: 1]
                case "2dafc052-752e-4ac8-93ee-193d7528e29b": name = "00m_over_hall"; break; //[cite: 1]
                case "8cf7f390-76b8-4239-b59b-52bbb603718f": name = "00o_over_hall"; break; //[cite: 1]
                case "c9bf9c12-5207-4fdd-aff1-fb0c65e7169d": name = "00t_over_hall"; break; //[cite: 1]
                case "0676e13f-15cf-4bc4-a058-5e056be4cfa4": name = "01_Over_mainplaza"; break; //[cite: 1]
                case "d674e2b2-fb22-49d5-a6bd-50d31cfde414": name = "01_over_pickup_spacejump"; break; //[cite: 1]
                case "527875db-9d49-40d6-9357-da0fb9931e37": name = "02_Over_halfpipe"; break; //[cite: 1]
                case "100e1d59-0c94-4912-80b5-49e5d3844b98": name = "03_IntroUnderwater_elevator"; break; //[cite: 1]
                case "48e5b6c6-a7a2-474a-963a-099c4d638b56": name = "03_over_pickup"; break; //[cite: 1]
                case "b0a10c6e-130c-43e9-83be-4e54813b7e2e": name = "03_Over_rootcave"; break; //[cite: 1]
                case "0d78132d-eafe-44d4-bee1-1029e0d5aee9": name = "04_IntroUnderwater_Specimen_Chamber"; break; //[cite: 1]
                case "29637ae5-5fed-4ced-881c-0f64aae3c234": name = "04_over_pickup"; break; //[cite: 1]
                case "edd6970a-3cc0-4f4c-b536-d3886f5aec60": name = "04_Over_treeroom"; break; //[cite: 1]
                case "aa00da17-cedf-4a33-82d3-678ee19f8193": name = "05_IntroUnderwaterZoo"; break; //[cite: 1]
                case "dcdd0b66-bea4-43c6-b2b7-913e727d532f": name = "05_Over_xrayroom"; break; //[cite: 1]
                case "9766fead-f280-4420-aabe-35b6f195c8e5": name = "06_IntroUnderwater_Freight_Lifts"; break; //[cite: 1]
                case "85bdfc5f-796c-4e03-bf8c-71cbef9ec741": name = "06_introunderwater_savestation"; break; //[cite: 1]
                case "dbb0d53c-dcf6-4d58-ae5a-3b97a559601e": name = "06_IntroUnderwater_To_Reactor"; break; //[cite: 1]
                case "fc7a9cf2-7932-451b-b994-fe27ae0d46d0": name = "06_Over_crashed_ship"; break; //[cite: 1]
                case "b1b9f04d-55d1-4565-a66c-d4cb12692090": name = "06_over_hall_crashedship"; break; //[cite: 1]
                case "c93de09d-f165-4481-8990-afd34da03c68": name = "07_IntroUnderwater_Reactor"; break; //[cite: 1]
                case "5722350a-e985-4ae3-b96b-36322622b93f": name = "07_Over_Stonehenge"; break; //[cite: 1]
                case "1e75a364-c829-47b2-84a8-a92a1f3e76cf": name = "07_over_Stonehenge_hall"; break; //[cite: 1]
                case "fc4b2719-cae0-4e92-a18d-1050b6bd8e81": name = "08a_IntroUnderwater_ventshaft"; break; //[cite: 1]
                case "db389196-0dad-4136-995a-7d55959f20eb": name = "08b_IntroUnderwater_ventshaft"; break; //[cite: 1]
                case "0be73d57-3f5b-4287-bf60-cfe6f3de43af": name = "mapstation"; break; //[cite: 1]
                case "914061a2-c17a-4826-94f5-be4faf90cae7": name = "!RuinsWorld"; break; //[cite: 1]
                case "fb28a118-3189-4b24-b212-b93369c45ec7": name = "0_Elev_Lava_B"; break; //[cite: 1]
                case "92f21343-0369-42d3-90ca-98396710725e": name = "0_Elev_Over_A"; break; //[cite: 1]
                case "8de242c2-a4d0-44b0-9d48-0b5b35f1c982": name = "0_Elev_Over_E"; break; //[cite: 1]
                case "2d6fcf34-f7ba-4383-a0df-660fe25b1c6d": name = "0_Elev_Over_F"; break; //[cite: 1]
                case "1dda2db8-e420-4710-8dbb-b9918822d727": name = "0b_connect_tunnel"; break; //[cite: 1]
                case "e832dec9-8734-47e8-8dd4-9d39d5529de1": name = "0c_connect_tunnel"; break; //[cite: 1]
                case "8f9a27fc-bcd2-437f-b5b4-3b847b18652a": name = "0d_connect_tunnel"; break; //[cite: 1]
                case "b0cec942-838c-4fe0-8d38-af6353dd0acd": name = "0e_connect_tunnel"; break; //[cite: 1]
                case "8abf2c3b-c2f2-4537-adc7-cc597beb9d53": name = "0f_connect_tunnel"; break; //[cite: 1]
                case "2ce93dd3-aee8-4553-b1e9-ac8616c0fc31": name = "0h_connect_tunnel"; break; //[cite: 1]
                case "cfae6212-7a63-46ad-8641-142e14dd9708": name = "0i_connect_tunnel"; break; //[cite: 1]
                case "1f7d39ca-326c-4d35-97c2-81aa078ec2e8": name = "0j_connect_tunnel"; break; //[cite: 1]
                case "1bfac32c-22ec-4fa9-b4d6-3e9e7c71f142": name = "0k_connect_tunnel"; break; //[cite: 1]
                case "6dad8301-de7d-42c8-8f2b-1baf56d462f8": name = "0l_connect_tunnel"; break; //[cite: 1]
                case "ca7b6ace-91de-49ba-962a-0e35bc7474a0": name = "0m_connect_tunnel"; break; //[cite: 1]
                case "c4d4d737-10ec-4c28-a95b-4c7481bf8e12": name = "0p_connect_tunnel"; break; //[cite: 1]
                case "e443c8a9-b1ae-4812-8006-5f5bf1baf34d": name = "0q_connect_tunnel"; break; //[cite: 1]
                case "7ad347c1-f236-44e8-ba84-be78fa50a6df": name = "0s_connect_tunnel"; break; //[cite: 1]
                case "b7b771ab-80d9-4a53-8cf6-9531a9a2ee16": name = "0t_connect_tunnel"; break; //[cite: 1]
                case "fccf0519-6cd1-451b-a5a5-ab4fd55a1d65": name = "0u_connect_tunnel"; break; //[cite: 1]
                case "bf0dd9f3-cf12-4700-90a5-0d26e399c5a5": name = "0v_connect_tunnel"; break; //[cite: 1]
                case "8f292228-700f-485e-867e-327ab4e0b4bc": name = "0w_connect_tunnel"; break; //[cite: 1]
                case "abda12bd-d2a8-4d55-81ab-60e2a6df17aa": name = "1_mainplaza"; break; //[cite: 1]
                case "f2b1db3a-ab41-4de9-a92b-237513b7092f": name = "1_savestation"; break; //[cite: 1]
                case "94519bc7-f6ed-40ce-a464-ade1988d19be": name = "1_special"; break; //[cite: 1]
                case "762eb17b-9510-47d3-b128-84c1e15d50f7": name = "1a_morphball_shrine"; break; //[cite: 1]
                case "334e8ff9-a638-4e8a-90d1-21d81ced031a": name = "1a_morphballtunnel2"; break; //[cite: 1]
                case "deef3a29-966a-4ff5-856e-8a3c0adca7ef": name = "2_mainhall"; break; //[cite: 1]
                case "446bf05f-f776-433f-9733-c982f4034da5": name = "2_savestation"; break; //[cite: 1]
                case "86e6497c-3f68-4ed8-981f-31417a010469": name = "3_monkey_lower"; break; //[cite: 1]
                case "c80a81f7-0baf-4951-b4e0-799809cc8bc1": name = "3_monkey_upper"; break; //[cite: 1]
                case "21dd5241-c4e7-4cca-a3ff-1247b1b97bc1": name = "3_savestation"; break; //[cite: 1]
                case "0e50ef31-30fb-47be-8a75-9e616851e714": name = "4_maproom_D"; break; //[cite: 1]
                case "450850b5-4ab3-4774-ab85-7706cb524b09": name = "4_monkey_hallway"; break; //[cite: 1]
                case "aac5cd74-51dc-49df-a44f-f817d258bca6": name = "5_bathhall"; break; //[cite: 1]
                case "43482cfa-27d2-4b41-8a25-c4f7ce29ee2b": name = "06_grapgallery"; break; //[cite: 1]
                case "71d72fa8-811a-4739-8a7a-864e8d29cf52": name = "7_ruinedroof"; break; //[cite: 1]
                case "8f6016fa-446b-4df7-812a-b3032adc6209": name = "8_courtyard"; break; //[cite: 1]
                case "e941295b-6b74-4bfd-9101-e5bf109b0007": name = "10_coreentrance"; break; //[cite: 1]
                case "36bcf48d-879c-4709-a948-933d404be42b": name = "11_12_connect"; break; //[cite: 1]
                case "95fba619-cba0-4180-af7d-aa47c356193a": name = "11_wateryhall"; break; //[cite: 1]
                case "6e5bc4cc-08fa-401a-93e1-9a5b229817a6": name = "12_monkeyShaft"; break; //[cite: 1]
                case "f63fe54a-cb74-4a8d-b21e-7fccd0700980": name = "14_tl_base01"; break; //[cite: 1]
                case "4db5529a-1b6c-4cf4-a21d-a4e544491e61": name = "14_tl_room01_02"; break; //[cite: 1]
                case "4adbb624-8e35-44d6-a1d3-eef448c17d1b": name = "15_energycore"; break; //[cite: 1]
                case "1663c700-c241-4ddb-a83c-2853331d8ff1": name = "16_furnaces"; break; //[cite: 1]
                case "353b3296-d668-45b5-9bf7-3a1e562eaf41": name = "17_ChozoBowling"; break; //[cite: 1]
                case "696ca819-26a0-402c-9f22-2a62b4c29d9b": name = "17_Connect"; break; //[cite: 1]
                case "bf18a651-ac91-4ae0-93eb-610478a7f93b": name = "18_halfpipe"; break; //[cite: 1]
                case "52672d6b-78c2-4298-a6e4-d3b58719da99": name = "18_halfpipe_connect_A"; break; //[cite: 1]
                case "6982aa46-4383-4e08-8098-5b8fa04ddefd": name = "18_halfpipe_connect_B"; break; //[cite: 1]
                case "1585ccd2-0f6b-4991-971c-c12451759495": name = "19_hivetotem"; break; //[cite: 1]
                case "446d2144-30ff-415f-9d11-913fad50fb6d": name = "20_pickuproom"; break; //[cite: 1]
                case "e3f963e0-509b-4725-9557-6e6fb10f9269": name = "20_reflecting_pool"; break; //[cite: 1]
                case "c8589215-9df1-4a71-81b8-0889bb0dc172": name = "22_flaahgraChamber"; break; //[cite: 1]
                case "02a46c20-3842-44b9-8539-00460a243a00": name = "99_some_hallway"; break; //[cite: 1]
                case "0b13f751-f62f-4f70-8c1f-07e738cd14e3": name = "generic_x01"; break; //[cite: 1]
                case "e554c3c5-82aa-47b4-bf7f-cb23f906d646": name = "generic_x02"; break; //[cite: 1]
                case "3fcfe32c-50e8-4b1f-97d3-dd3c11d179d1": name = "generic_x04"; break; //[cite: 1]
                case "c2354480-762f-4e53-838e-cff88ec87bda": name = "generic_x05"; break; //[cite: 1]
                case "ea34c996-230e-45c7-8709-eed2f2cc29c9": name = "generic_x06"; break; //[cite: 1]
                case "ae094f2a-9ee2-43db-9756-f5f900cf445a": name = "generic_z02"; break; //[cite: 1]
                case "cf7cfadd-96d3-481c-aa73-5fa7158b37ac": name = "generic_z03"; break; //[cite: 1]
                case "ce44a797-c442-4128-8372-694626a83722": name = "end_cinema"; break; //[cite: 1]
            }

            return name;
        }
    }
}
