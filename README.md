# Blueprint To C++

A tool that converts Unreal Engine Blueprints to C++ code.

Powered by [CUE4Parse](https://github.com/FabianFG/CUE4Parse)

## Installation

1. Clone the repository:
    ```bash
    git clone https://github.com/Krowe-moh/BlueprintToCpp.git --recursive
    ```

2. Open the solution file in your IDE and build the project.

## Usage

1. Run the executable to automatically create a `config.json` file.

2. Configure the options in `config.json`:

Example:
```json
{
  "PakFolderPath": "C:/Program Files/Epic Games/Fortnite/FortniteGame/Content/Paks",
  "BlueprintPath": "FortniteGame/Plugins/GameFeatures/DebugUI/Content/BP_TextChatV3.uasset",
  "OodlePath": "C:/Users/krowe/BlueprintToCpp/oo2core_5_win64.dll",
  "ZlibPath": "",
  "UsmapPath": "C:/Users/krowe/BlueprintToCpp/++Fortnite+Release-37.00-CL-44501951-Windows_oo.usmap",
  "Version": "GAME_UE5_LATEST"
}
```

3. Run `Main.exe` to start the conversion.

## AES

If you want to set up AES, run the program once (with the config set), then modify the `aes.json` file that is created.

## Output

Note: the C++ output may not be 100% accurate.

## Issues

If you encounter any issues, please submit them [here](https://github.com/Krowe-moh/BlueprintToCpp/issues).

## Contributing

Feel free to submit issues, fork the repository, and create pull requests for any improvements.
