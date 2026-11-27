# 编译与部署

## 编译

在当前目录运行：

```powershell
dotnet build RimSimRestaurantExtension.csproj -c Release
```

预期结果：

- 编译通过，不出现缺失 DLL 引用错误。
- 使用 .NET 10 SDK，生成的程序集位于 `..\..\1.7\Assemblies\RimSimRestaurantExtension.dll`。
- 项目通过相对路径 `..\..\..\RimSimManagementFramework\Source\SimManagementLib\SimManagementLib.csproj` 引用同仓库框架工程，自动先编译框架，不复制框架 DLL 到餐厅目录。
- 源码只在模组根目录 `Source` 维护一份；只构建 1.7，1.6 的运行文件冻结。

## 加载

仓库根目录和各模组目录中的 `deploy_modSelf.bat` 共用同一入口。它构建统一解决方案，将框架、餐厅和旅馆分别安装到游戏的 `Mods` 目录。餐厅在游戏中只依赖框架。

## 排错

- 编译失败时，先检查 .NET 10 SDK、RimWorld 1.7 游戏根目录 DLL、Harmony 1.7 和同仓库框架工程是否存在。自定义引用位置可在仓库根目录运行 `compile_modSelf.ps1 -RimWorldPath <游戏目录> -HarmonyPath <Harmony DLL>`。
- 游戏未识别时，检查安装目录中是否直接存在 `About/About.xml`，避免额外嵌套一层同名目录。
