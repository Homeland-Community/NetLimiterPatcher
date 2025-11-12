using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

class Program
{
    static void Main(string[] args)
    {
        // Check if running as administrator
        if (!IsAdministrator())
        {
            // Restart with admin privileges
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "runas" // This triggers UAC prompt
                };
                
                Process.Start(processInfo);
            }
            catch
            {
                Console.WriteLine("Error: Administrator privileges required!");
                Console.ReadKey();
            }
            return;
        }

        string dllPath = "NetLimiter.dll";
        string backupPath = "NetLimiter-bc.dll";
        string tempOutputPath = "NetLimiter_temp.dll";

        try
        {
            // First, verify that the file exists
            if (!File.Exists(dllPath))
            {
                Console.WriteLine($"Error: {dllPath} not found!");
                Console.ReadKey();
                return;
            }

            // Load assembly for validation
            var readerParameters = new ReaderParameters { ReadWrite = false };
            AssemblyDefinition assembly;
            
            try
            {
                assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to read assembly: {ex.Message}");
                Console.ReadKey();
                return;
            }

            // Find required types for validation
            var nlServiceTemp = assembly.MainModule.Types
                .FirstOrDefault(t => t.FullName == "NetLimiter.Service.NLServiceTemp");
            var nlLicense = assembly.MainModule.Types
                .FirstOrDefault(t => t.FullName == "NetLimiter.Service.NLLicense");

            if (nlServiceTemp == null || nlLicense == null)
            {
                assembly.Dispose();
                Console.WriteLine("Error: Required classes not found!");
                Console.ReadKey();
                return;
            }

            // Release file handle after validation and before stopping processes
            assembly.Dispose();
            
            StopNetLimiterProcesses();

            // Reload the assembly for patching
            assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);

            // Find the types again after reloading
            nlServiceTemp = assembly.MainModule.Types
                .FirstOrDefault(t => t.FullName == "NetLimiter.Service.NLServiceTemp");
            nlLicense = assembly.MainModule.Types
                .FirstOrDefault(t => t.FullName == "NetLimiter.Service.NLLicense");

            // Create backup
            File.Copy(dllPath, backupPath, true);

            // Track patch success
            bool patch1Success = false;
            bool patch2Success = false;
            bool patch3Success = false;

            // PATCH 1: Trial period
            var initLicenseMethod = nlServiceTemp.Methods
                .FirstOrDefault(m => m.Name == "InitLicense");

            if (initLicenseMethod != null && initLicenseMethod.Body != null)
            {
                var instructions = initLicenseMethod.Body.Instructions;
                for (int i = 0; i < instructions.Count; i++)
                {
                    var inst = instructions[i];
                    if (inst.OpCode == OpCodes.Ldc_R8 && 
                        inst.Operand is double doubleValue && 
                        doubleValue == 28.0)
                    {
                        inst.Operand = 99999.0;
                        patch1Success = true;
                        break;
                    }
                }
            }

            // PATCH 2: IsRegistered
            var constructors = nlLicense.Methods.Where(m => m.Name == ".ctor").ToList();
            foreach (var ctor in constructors)
            {
                if (ctor.Body == null) continue;
                var instructions = ctor.Body.Instructions;

                for (int i = 0; i < instructions.Count - 1; i++)
                {
                    var inst1 = instructions[i];
                    var inst2 = instructions[i + 1];

                    if (inst1.OpCode == OpCodes.Ldc_I4_0 &&
                        inst2.OpCode == OpCodes.Call &&
                        inst2.Operand != null &&
                        inst2.Operand.ToString().Contains("set_IsRegistered"))
                    {
                        var ilProcessor = ctor.Body.GetILProcessor();
                        var newInst = ilProcessor.Create(OpCodes.Ldc_I4_1);
                        ilProcessor.Replace(inst1, newInst);
                        patch2Success = true;
                        break;
                    }
                }
                if (patch2Success) break;
            }

            // PATCH 3: RegName
            var getRegName = nlLicense.Methods
                .FirstOrDefault(m => m.Name == "get_RegName");

            if (getRegName != null && getRegName.Body != null)
            {
                var ilProcessor = getRegName.Body.GetILProcessor();
                getRegName.Body.Instructions.Clear();
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldstr, "Linkosi"));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ret));
                patch3Success = true;
            }

            // Verify all patches succeeded
            if (!patch1Success || !patch2Success || !patch3Success)
            {
                string errors = "";
                if (!patch1Success) errors += "- Patch 1 (Trial period) failed\n";
                if (!patch2Success) errors += "- Patch 2 (IsRegistered) failed\n";
                if (!patch3Success) errors += "- Patch 3 (RegName) failed\n";
                assembly.Dispose();
                throw new Exception($"Patching failed:\n{errors}");
            }

            // Save to temporary file first
            assembly.Write(tempOutputPath);
            assembly.Dispose();

            // Replace the original file
            File.Delete(dllPath);
            File.Move(tempOutputPath, dllPath);

            // Silent success - exit without message
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.ReadKey();
        }
    }

    static bool IsAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void StopNetLimiterProcesses()
    {
        try
        {
            // Try multiple possible service names
            string[] serviceNames = { "nlsvc", "NetLimiter", "NetLimiter 5" };
            
            foreach (var serviceName in serviceNames)
            {
                try
                {
                    var stopService = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = $"stop \"{serviceName}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    var process = Process.Start(stopService);
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        process.Dispose();
                    }
                }
                catch
                {
                    // Try next service name
                }
            }

            // Wait for service to fully stop
            Thread.Sleep(2000);

            // Kill NLClientApp.exe process
            foreach (var process in Process.GetProcessesByName("NLClientApp"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    process.Dispose();
                }
                catch
                {
                    // Process might already be closed
                }
            }

            // Kill nlsvc.exe if still running
            foreach (var process in Process.GetProcessesByName("nlsvc"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    process.Dispose();
                }
                catch
                {
                    // Process might already be closed
                }
            }

            // Wait to ensure file locks are released
            Thread.Sleep(1000);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to stop NetLimiter processes: {ex.Message}");
        }
    }
}
