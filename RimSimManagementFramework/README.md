# RimSimManagementFramework

本目录是模拟经营框架 Mod 根目录，包含 `About`、`Content`、`LoadFolders.xml` 与 `1.6`。餐厅扩展位于同级目录 [RimSimRestaurantExtension](../RimSimRestaurantExtension/README.md)。

Git 仓库根目录为上一级 `RimSimFramework`。两个 Mod 共用编译和部署脚本，具体目录与参数见 [仓库说明](../README.md)。

运行本目录的 `compile_modSelf.bat` 会依次编译框架和餐厅。运行 `deploy_modSelf.bat` 或 `deploy_modSelf.ps1` 会先编译，再将两个 Mod 分别安装到 Steam 游戏的 `Mods` 目录。

框架源码位于 `1.6/Source/SimManagementLib`；共用解决方案 `SimManagementLib.slnx` 同时包含框架和同级餐厅工程。Debug 程序集位于 `1.6/Assemblies`。
