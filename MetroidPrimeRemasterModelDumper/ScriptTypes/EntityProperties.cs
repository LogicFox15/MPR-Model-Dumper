using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;
using SevenZip.CommandLineParser;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class EntityProperties
    {
        public bool active;
        public bool unknownFlag;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 blenderRotation;     // For lights

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

            Quaternion Q = GameEulerToBlenderQuaternion(script.rotation);
            Vector3 E = ExtractBlenderEuler(Q);
            script.blenderRotation = E;

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }

        public static Quaternion GameEulerToBlenderQuaternion(Vector3 gameEulerDegrees)
        {
            // 1. Convert degrees to radians
            float x = gameEulerDegrees.X * (MathF.PI / 180f);
            float y = gameEulerDegrees.Y * (MathF.PI / 180f);
            float z = gameEulerDegrees.Z * (MathF.PI / 180f);

            // 2. Create Quaternions for each axis
            Quaternion qX = Quaternion.CreateFromAxisAngle(Vector3.UnitX, x);
            Quaternion qY = Quaternion.CreateFromAxisAngle(Vector3.UnitY, y);
            Quaternion qZ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z);

            // 3. Combine in standard Game order 
            // (Note: If the object acts like a gyroscope and is wildly off, 
            // the game uses Z-Y-X order. Change this line to: qZ * qY * qX)
            //Quaternion qGame = qX * qY * qZ;
            Quaternion qGame = qZ * qY * qX;

            // 4. The Magic Handedness Flip
            // We swap Y and Z, invert Z, AND negate the vector parts to flip the rotation direction.
            return new Quaternion(
                 qGame.X,
                 -qGame.Z,
                 qGame.Y,
                 qGame.W
            );
        }

        public static Vector3 ExtractBlenderEuler(Quaternion q)
        {
            Vector3 euler = new Vector3();

            // Pitch (Y)
            float sinPitch = 2.0f * (q.W * q.Y - q.Z * q.X);
            euler.Y = MathF.Abs(sinPitch) >= 1.0f
                ? MathF.CopySign(MathF.PI / 2.0f, sinPitch)
                : MathF.Asin(sinPitch);

            // Roll (X)
            float sinRoll = 2.0f * (q.W * q.X + q.Y * q.Z);
            float cosRoll = 1.0f - 2.0f * (q.X * q.X + q.Y * q.Y);
            euler.X = MathF.Atan2(sinRoll, cosRoll);

            // Yaw (Z)
            float sinYaw = 2.0f * (q.W * q.Z + q.X * q.Y);
            float cosYaw = 1.0f - 2.0f * (q.Y * q.Y + q.Z * q.Z);
            euler.Z = MathF.Atan2(sinYaw, cosYaw);

            // Return in degrees
            return euler * (180f / MathF.PI);
        }
    }
}
