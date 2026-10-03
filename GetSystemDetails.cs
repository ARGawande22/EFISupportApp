using System;
using System.Data;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;


namespace EFISupportApp
{
    public class GetSystemDetails
    {
        public static string GenerateUniqueId()
        {
            string raw = string.Join("|",
                GetSystemUUID(),
                GetMotherboardSerial(),
                GetBiosSerial(),
                GetProcessorId()
            );

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hashBytes); // .NET 5+; use BitConverter for older versions
        }

        /// <summary>
        /// Motherboard/System UUID — burned into BIOS/UEFI (SMBIOS table).
        /// Survives OS reinstall/format. Changes only if motherboard is replaced.
        /// </summary>
        public static string GetSystemUUID()
        {
            return QueryWmi("Win32_ComputerSystemProduct", "UUID");
        }

        /// <summary>
        /// Motherboard serial number.
        /// </summary>
        public static string GetMotherboardSerial()
        {
            return QueryWmi("Win32_BaseBoard", "SerialNumber");
        }

        /// <summary>
        /// BIOS serial number — also firmware-level, survives format.
        /// </summary>
        public static string GetBiosSerial()
        {
            return QueryWmi("Win32_BIOS", "SerialNumber");
        }

        /// <summary>
        /// CPU identifier. Not always unique across units on modern CPUs,
        /// but adds another factor to the combined hash.
        /// </summary>
        public static string GetProcessorId()
        {
            return QueryWmi("Win32_Processor", "ProcessorId");
        }

        /// <summary>
        /// CPU identifier. Not always unique across units on modern CPUs,
        /// but adds another factor to the combined hash.
        /// </summary>
        public static string GetGetMachineName()
        {
            return QueryWmi("Win32_ComputerSystem", "Name");
        }

        private static string QueryWmi(string wmiClass, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var value = obj[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                // Log this in production — a failure here usually means
                // running in a restricted environment (some VMs, containers)
                Console.WriteLine($"WMI query failed for {wmiClass}.{property}: {ex.Message}");
            }
            return "UNKNOWN";
        }

    }
}
