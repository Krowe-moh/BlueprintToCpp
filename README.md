
<div align="right">
  <details>
    <summary >🌐 Language</summary>
    <div>
      <div align="center">
        <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=en">English</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=zh-CN">简体中文</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=zh-TW">繁體中文</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=ja">日本語</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=ko">한국어</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=hi">हिन्दी</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=th">ไทย</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=fr">Français</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=de">Deutsch</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=es">Español</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=it">Italiano</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=ru">Русский</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=pt">Português</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=nl">Nederlands</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=pl">Polski</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=ar">العربية</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=fa">فارسی</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=tr">Türkçe</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=vi">Tiếng Việt</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=id">Bahasa Indonesia</a>
        | <a href="https://openaitx.github.io/view.html?user=Krowe-moh&project=BlueprintToCpp&lang=as">অসমীয়া</
      </div>
    </div>
  </details>
</div>

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
    ```js
    {
      "PakFolderPath": "C:/Program Files/Epic Games/Fortnite/FortniteGame/Content/Paks",
      "BlueprintPath": "FortniteGame/Content/Athena/Cosmetics/Sprays/BP_SprayDecal.uasset",
      "OodlePath": "C:/Users/krowe/BlueprintToCpp/oo2core_5_win64.dll",
      "ZlibPath ": "", // leave blank if no zlib compression
      "UsmapPath": "C:/Users/krowe/BlueprintToCpp/++Fortnite+Release-33.20-CL-39082670-Windows_oo.usmap",
      "Version": "GAME_UE5_LATEST"
    }
    ```

3. Run `Main.exe` to start the conversion.

## AES

If you want to set up AES, run the program once (with the config set), then modify the `aes.json` file that is created.

## Output

The decompiled blueprint will be output as `Output.cpp` (this has changed to output as folder hierarchy, will have a option to disable soon).

Note: Currently, this tool does not support all expressions, and the C++ output may not be 100% accurate.

## Issues

If you encounter any issues, please submit them [here](https://github.com/Krowe-moh/BlueprintToCpp/issues).

## Contributing

Feel free to submit issues, fork the repository, and create pull requests for any improvements.
