using System;
using System.Globalization;

namespace BitMono.Obfuscation.TestCases.LocalVariableEncoding
{
    public static class Program
    {
        private const string ExpectedOutput =
            "0,1|-128,127|0,255|-32768,32767|0,65535|-2147483648,2147483647|0,4294967295|" +
            "-9223372036854775808,9223372036854775807|0,18446744073709551615|0,65535|5|58|" +
            "10000000022|18,14|63|19,38|98|42|15|25.0|5";

        public static int Main()
        {
            var branchResults = $"{BranchMerge(-9)},{BranchMerge(4)}";
            var exceptionResults = $"{ExceptionFlow(3)},{ExceptionFlow(-1)}";
            var floatingPointResult = FloatingPointLocal().ToString("0.0", CultureInfo.InvariantCulture);
            var structureResult = StructureLocal().Day;
            var actualOutput = string.Join(
                "|",
                IntegralBoundaries(),
                EnumLocal(),
                RepeatedWrites(),
                TwoLiveLocals(),
                branchResults,
                LoopAccumulation(7),
                exceptionResults,
                ManyLocalSlots(),
                AddressTaken(),
                ReferenceLocal().Length,
                floatingPointResult,
                structureResult);

            Console.WriteLine(actualOutput);
            return StringComparer.Ordinal.Equals(actualOutput, ExpectedOutput) ? 0 : 1;
        }

        public static string IntegralBoundaries()
        {
            bool booleanMinimum = false;
            bool booleanMaximum = true;
            sbyte signedByteMinimum = sbyte.MinValue;
            sbyte signedByteMaximum = sbyte.MaxValue;
            byte byteMinimum = byte.MinValue;
            byte byteMaximum = byte.MaxValue;
            short signedInt16Minimum = short.MinValue;
            short signedInt16Maximum = short.MaxValue;
            ushort unsignedInt16Minimum = ushort.MinValue;
            ushort unsignedInt16Maximum = ushort.MaxValue;
            int signedInt32Minimum = int.MinValue;
            int signedInt32Maximum = int.MaxValue;
            uint unsignedInt32Minimum = uint.MinValue;
            uint unsignedInt32Maximum = uint.MaxValue;
            long signedInt64Minimum = long.MinValue;
            long signedInt64Maximum = long.MaxValue;
            ulong unsignedInt64Minimum = ulong.MinValue;
            ulong unsignedInt64Maximum = ulong.MaxValue;
            char characterMinimum = char.MinValue;
            char characterMaximum = char.MaxValue;

            return string.Join(
                "|",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(booleanMinimum ? 1 : 0)},{(booleanMaximum ? 1 : 0)}"),
                string.Create(CultureInfo.InvariantCulture, $"{signedByteMinimum},{signedByteMaximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{byteMinimum},{byteMaximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{signedInt16Minimum},{signedInt16Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{unsignedInt16Minimum},{unsignedInt16Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{signedInt32Minimum},{signedInt32Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{unsignedInt32Minimum},{unsignedInt32Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{signedInt64Minimum},{signedInt64Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{unsignedInt64Minimum},{unsignedInt64Maximum}"),
                string.Create(CultureInfo.InvariantCulture, $"{(int)characterMinimum},{(int)characterMaximum}"));
        }

        public static int EnumLocal()
        {
            DayOfWeek day = DayOfWeek.Monday;
            day = DayOfWeek.Friday;
            return (int)day;
        }

        public static int RepeatedWrites()
        {
            int value = 7;
            value *= 3;
            value -= 5;
            value ^= 42;
            return value;
        }

        public static long TwoLiveLocals()
        {
            int left = 17;
            long right = 10000000000L;

            left += 5;
            right -= left;
            return right + left * 2L;
        }

        public static int BranchMerge(int value)
        {
            int result;
            if (value < 0)
                result = -value;
            else
                result = value + 3;

            result *= 2;
            return result;
        }

        public static int LoopAccumulation(int limit)
        {
            int total = 0;
            for (int index = 0; index < limit; index++)
                total += index * 3;

            return total;
        }

        public static int ExceptionFlow(int value)
        {
            int result = 4;
            try
            {
                if (value < 0)
                    throw new InvalidOperationException();

                result = checked(result * value);
            }
            catch (InvalidOperationException)
            {
                result = 31;
            }
            finally
            {
                result += 7;
            }

            return result;
        }

        public static int ManyLocalSlots()
        {
            int first = 1;
            int second = 2;
            int third = 3;
            int fourth = 4;
            int fifth = 5;
            int sixth = 6;
            int seventh = 7;
            int eighth = 8;

            first += eighth;
            second += seventh;
            third += sixth;
            fourth += fifth;
            fifth += fourth;
            sixth += third;
            seventh += second;
            eighth += first;

            return first + second + third + fourth + fifth + sixth + seventh + eighth;
        }

        public static int AddressTaken()
        {
            int value = 40;
            AddTwo(ref value);
            return value;
        }

        public static string ReferenceLocal()
        {
            string value = "stack";
            value += "-reference";
            return value;
        }

        public static double FloatingPointLocal()
        {
            double value = 12.5;
            value *= 2.0;
            return value;
        }

        public static DateTime StructureLocal()
        {
            DateTime value = new DateTime(2001, 2, 3);
            value = value.AddDays(2.0);
            return value;
        }

        private static void AddTwo(ref int value)
        {
            value += 2;
        }
    }
}
