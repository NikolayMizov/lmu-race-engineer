Apex HUD fonts. The STATIC .ttf files here are embedded into the single-file exe
(no install needed on the target PC).

Saira Condensed  (https://fonts.google.com/specimen/Saira+Condensed)
  SairaCondensed-Regular.ttf, -SemiBold.ttf, -Bold.ttf  (used by the theme)
  the other weights (Thin..Black) are harmless extras and also embed.

JetBrains Mono   (https://fonts.google.com/specimen/JetBrains+Mono)
  JetBrainsMono-Regular.ttf, -Bold.ttf

The .csproj embeds Fonts\*.ttf as <Resource>, EXCLUDING *VariableFont*.ttf: WPF has
poor variable-font support and a variable "JetBrains Mono" would collide with the
static Regular/Bold. So variable-font downloads may sit here but are not bundled.

The pack:// URIs in Themes/ApexTheme.xaml reference the fonts by internal family name
("Saira Condensed", "JetBrains Mono") and pick weight via FontWeight. If a family is
missing, WPF falls back to Segoe UI / Consolas.
