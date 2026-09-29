using AvaloniaToolbox.Core.IO;
using DKCTF;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetroidPrimeRemasterModelDumper.FileData
{
    public class LoadUnitLogic
    {
        public ushort tokenCount;
        public List<LoadUnitToken> tokens = new List<LoadUnitToken>();

        public static LoadUnitLogic Read(FileReader reader, uint byteSize)
        {
            long startpos =  reader.Position;

            LoadUnitLogic logic = new LoadUnitLogic();
            logic.tokenCount = reader.ReadUInt16();
            for (int i = 0; i < logic.tokenCount; i++)
            {
                logic.tokens.Add(LoadUnitToken.Read(reader));
            }
            
            return logic;
        }
    }

    public class LoadUnitToken
    {
        public ushort kind;

        public CObjectId environmentVarId;
        public string debugName;
        public ELoadUnitLogicOperator op;
        public uint value;

        public static LoadUnitToken Read(FileReader reader)
        {
            LoadUnitToken token = new LoadUnitToken();
            token.kind = reader.ReadUInt16();

            if(token.kind == 0)
            {
                token.environmentVarId = reader.ReadStruct<CObjectId>();
                token.debugName = BinaryExtensions.ReadCStringFixed(reader);
            }
            else if( token.kind == 1)
            {
                token.op = (ELoadUnitLogicOperator)reader.ReadUInt16();
            }
            else if (token.kind == 2)
            {
                token.value = reader.ReadUInt32();
            }
            return token;
        }
    }

    public enum ELoadUnitLogicOperator : ushort
    {
        LoadUnitEqual = 0,
        LoadUnitNotEqual = 1,
        LoadUnitLess = 2,
        LoadUnitGreater = 3,
        LoadUnitLessEqual = 4,
        LoadUnitGreaterEqual = 5,
        LoadUnitAnd = 6,
        LoadUnitOr = 7
    }
}
