# 编译与部署

## 编译

在当前目录运行：

```powershell
msbuild RimSimRestaurantExtension.csproj /p:Configuration=Debug
```

预期结果：

- 编译通过，不出现缺失 DLL 引用错误。
- 生成的程序集位于 `..\..\Assemblies\RimSimRestaurantExtension.dll`。
- 项目通过相对路径 `..\..\..\..\RimSimManagementFramework\1.6\Source\SimManagementLib\SimManagementLib.csproj` 引用同仓库框架工程，MSBuild 自动先编译框架，不复制框架 DLL 到餐厅目录。

## 加载

两个 Mod 是同级目录，统一由 `RimSimFramework` 根目录的 Git 仓库管理。运行根目录或任一 Mod 目录的 `deploy_modSelf.bat`，会先编译再部署框架与餐厅到 Steam 游戏的两个独立 Mod 目录。脚本不启动游戏。

## 排错

- 编译失败时，先检查 `E:\捷豹\rimworlddll` 和同仓库框架工程是否存在。
- 游戏未识别时，检查安装目录中是否直接存在 `About/About.xml`，避免额外嵌套一层同名目录。
