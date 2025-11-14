# NetLimiter Patcher

A tool to patch NetLimiter "Licence".

## Compatibility

This patcher is currently designed for **NetLimiter 5.3.25.0** and may work with older versions as well.

## Installation Methods

### Method 1: Install via Scoop (Recommended)

If you have Scoop installed and the `Homeland-Community` bucket added, simply run:

```bash
scoop install homeland/netlimiter
```

### Method 2: Download Pre-built Executable

1. Download the latest release from the [Releases](https://github.com/Homeland-Community/NetLimiterPatcher/releases) page
2. Place the `NetLimiterPatcher.exe` file in your NetLimiter installation directory

### Method 3: Build from Source

#### Prerequisites

- .NET SDK
- Required NuGet packages:
  - `Mono.Cecil`
  - `Fody`
  - `Costura.Fody`

#### Build Instructions

1. Clone the repository:
```bash
git clone https://github.com/Homeland-Community/NetLimiterPatcher.git
cd NetLimiterPatcher
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Build the project:
```bash
dotnet build -c Release -r win-x64
```

4. Copy the generated `NetLimiterPatcher.exe` file to your NetLimiter installation directory

#### Customization

You can customize the registration name by modifying this section in the code before building:

```csharp
// PATCH 3: RegName
var getRegName = nlLicense.Methods
    .FirstOrDefault(m => m.Name == "get_RegName");
if (getRegName != null && getRegName.Body != null)
{
    var ilProcessor = getRegName.Body.GetILProcessor();
    getRegName.Body.Instructions.Clear();
    ilProcessor.Append(ilProcessor.Create(OpCodes.Ldstr, "Linkosi")); // Change "Linkosi" to your desired name
    ilProcessor.Append(ilProcessor.Create(OpCodes.Ret));
    patch3Success = true;
}
```

Simply replace `"Linkosi"` with your preferred registration name.

## Usage

Run `NetLimiterPatcher.exe` in the NetLimiter directory. The tool will automatically apply the necessary patches.
