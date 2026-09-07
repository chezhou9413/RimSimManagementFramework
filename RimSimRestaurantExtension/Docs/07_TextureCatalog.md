# 餐厅贴图资源清单

来源：用户提供的 `餐厅组.zip`，共 61 张 PNG，按英文名称整理到餐厅 Mod 的 `1.6/Textures`。本次入库保持原图字节、尺寸和透明通道不变。

## 命名与目录规则

- 文件统一使用 `RSR_` 前缀和英文用途名称，减少与其他 Mod 的贴图路径冲突。
- 建筑资源根目录：`1.6/Textures/Things/Building/Restaurant`，按家具类型分子目录。
- 独立图标目录：`1.6/Textures/UI/Icons/Buildings/Restaurant`。
- 正面为 `_south`，背面为 `_north`，侧面为 `_east`。原包没有独立西向图，接入时可在允许翻转的情况下复用东向图。
- 多方向染色蒙版紧接方向加 `m`，例如 `RSR_DiningChair_southm.png`；单图、图集和图标的染色蒙版使用 `_m`，例如 `RSR_DiningTable1x1_m.png`。后缀已按原版 `Graphic_Multi` 与 `Graphic_Single` 源码核对。
- 1×2 餐桌按默认纵向占地命名：竖向为 `_north`，横向为 `_east`；南向与西向可由原版方向复用处理。
- `_Atlas` 表示 1024×1024 的 4×4 拼接图集，每块 256×256；吧台桌和寿司台需要按图集方式接入。
- `_BaseAtlas` 表示寿司台无传送带表面的底座图集。原包没有单独提供与该名称对应的染色蒙版。
- `Masks/RSR_SushiConveyor_BeltRegion.png` 是传送带绘制区域蒙版，单独命名；它与普通材质染色蒙版的用途不同。
- `Animation/RSR_SushiConveyor_Belt_Frame01.png` 至 `Frame03.png` 保持原始动画顺序。

## 资源分组

| 目录名称 | 中文用途 | 内容 |
| --- | --- | --- |
| `BarStool` | 高脚椅 | 6 张方向主图及蒙版 |
| `Refrigerator` | 冰箱 | 6 张方向主图及蒙版 |
| `BoothSofa` | 卡座沙发 | 6 张方向主图及蒙版 |
| `DiningChair` | 餐椅 | 6 张方向主图及蒙版 |
| `WallCabinet` | 壁挂橱柜 | 6 张方向主图及蒙版 |
| `WallWineCabinet` | 壁挂酒柜 | 6 张方向主图及蒙版 |
| `KitchenStorageCabinet` | 落地厨房杂物柜 | 6 张方向主图及蒙版 |
| `DiningTable1x1` | 1×1 餐桌 | 主图与染色蒙版 |
| `DiningTable1x2` | 1×2 餐桌 | 竖向、横向主图及蒙版 |
| `BarCounter` | 吧台桌 | 拼接图集、独立图标及各自蒙版 |
| `SushiConveyor` | 旋转寿司台 | 完整图集、底座图集、图标、蒙版及 3 帧传送带动画 |

除旋转寿司台外的 10 类资源已经绑定 `RSR_` 建筑 Def，位于 `Defs/Buildings/RestaurantFurniture.xml` 和 `RestaurantStorage.xml`。吧台使用自定义同类拼接 Graphic 与独立图标；壁挂柜使用反向材质与依墙偏移。旋转寿司台保留资源，不加载建筑或动画。

餐桌建筑 Def 分别为 `RSR_DiningTableSquare` 与 `RSR_DiningTableRectangular`。ThingDef 名称不能以数字结尾；贴图路径不受此限制，保留已整理的 1x1、1x2 文件名。

## 原名与新路径对照

下表的目标路径相对于餐厅 Mod 的 `1.6/Textures`。

| 原压缩包路径 | 英文目标路径 | 尺寸 |
| --- | --- | --- |
| 吧台桌高脚椅组/高脚椅（正面）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_south.png` | 256×256 |
| 吧台桌高脚椅组/高脚椅（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_southm.png` | 256×256 |
| 吧台桌高脚椅组/高脚椅（背面）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_north.png` | 256×256 |
| 吧台桌高脚椅组/高脚椅（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_northm.png` | 256×256 |
| 吧台桌高脚椅组/高脚椅（侧面）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_east.png` | 256×256 |
| 吧台桌高脚椅组/高脚椅（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/BarStool/RSR_BarStool_eastm.png` | 256×256 |
| 冰箱/冰箱（正面）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_south.png` | 256×256 |
| 冰箱/冰箱（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_southm.png` | 256×256 |
| 冰箱/冰箱（背面）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_north.png` | 256×256 |
| 冰箱/冰箱（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_northm.png` | 256×256 |
| 冰箱/冰箱（侧面）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_east.png` | 256×256 |
| 冰箱/冰箱（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/Refrigerator/RSR_Refrigerator_eastm.png` | 256×256 |
| 常规桌椅组/卡座式沙发（正面）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_south.png` | 512×512 |
| 常规桌椅组/卡座式沙发（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_southm.png` | 512×512 |
| 常规桌椅组/卡座式沙发（背面）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_north.png` | 512×512 |
| 常规桌椅组/卡座式沙发（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_northm.png` | 512×512 |
| 常规桌椅组/卡座式沙发（侧面）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_east.png` | 512×512 |
| 常规桌椅组/卡座式沙发（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/BoothSofa/RSR_BoothSofa_eastm.png` | 512×512 |
| 常规桌椅组/餐椅（正面）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_south.png` | 256×256 |
| 常规桌椅组/餐椅（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_southm.png` | 256×256 |
| 常规桌椅组/餐椅（背面）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_north.png` | 256×256 |
| 常规桌椅组/餐椅（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_northm.png` | 256×256 |
| 常规桌椅组/餐椅（侧面）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_east.png` | 256×256 |
| 常规桌椅组/餐椅（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/DiningChair/RSR_DiningChair_eastm.png` | 256×256 |
| 橱柜组/壁挂橱柜（正面）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_south.png` | 256×256 |
| 橱柜组/壁挂橱柜（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_southm.png` | 256×256 |
| 橱柜组/壁挂橱柜（背面）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_north.png` | 256×256 |
| 橱柜组/壁挂橱柜（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_northm.png` | 256×256 |
| 橱柜组/壁挂橱柜（侧面）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_east.png` | 256×256 |
| 橱柜组/壁挂橱柜（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/WallCabinet/RSR_WallCabinet_eastm.png` | 256×256 |
| 橱柜组/壁挂酒柜（正面）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_south.png` | 256×256 |
| 橱柜组/壁挂酒柜（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_southm.png` | 256×256 |
| 橱柜组/壁挂酒柜（背面）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_north.png` | 256×256 |
| 橱柜组/壁挂酒柜（背面）（背面遮罩）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_northm.png` | 256×256 |
| 橱柜组/壁挂酒柜（侧面）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_east.png` | 256×256 |
| 橱柜组/壁挂酒柜（侧面）（蒙版遮罩）.png | `Things/Building/Restaurant/WallWineCabinet/RSR_WallWineCabinet_eastm.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（正面）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_south.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（正面）（遮罩蒙版）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_southm.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（背面）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_north.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（背面）（遮罩蒙版）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_northm.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（侧面）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_east.png` | 256×256 |
| 橱柜组/落地厨房杂物柜（侧面）（遮罩蒙版）.png | `Things/Building/Restaurant/KitchenStorageCabinet/RSR_KitchenStorageCabinet_eastm.png` | 256×256 |
| 常规桌椅组/1x1桌子.png | `Things/Building/Restaurant/DiningTable1x1/RSR_DiningTable1x1.png` | 256×256 |
| 常规桌椅组/1x1桌子（遮罩蒙版）.png | `Things/Building/Restaurant/DiningTable1x1/RSR_DiningTable1x1_m.png` | 256×256 |
| 常规桌椅组/1x2桌子（竖向）.png | `Things/Building/Restaurant/DiningTable1x2/RSR_DiningTable1x2_north.png` | 512×512 |
| 常规桌椅组/1x2桌子（竖向）（遮罩蒙版）.png | `Things/Building/Restaurant/DiningTable1x2/RSR_DiningTable1x2_northm.png` | 512×512 |
| 常规桌椅组/1x2桌子（横向）.png | `Things/Building/Restaurant/DiningTable1x2/RSR_DiningTable1x2_east.png` | 512×512 |
| 常规桌椅组/1x2桌子（横向）（遮罩蒙版）.png | `Things/Building/Restaurant/DiningTable1x2/RSR_DiningTable1x2_eastm.png` | 512×512 |
| 吧台桌高脚椅组/吧台桌.png | `Things/Building/Restaurant/BarCounter/RSR_BarCounter_Atlas.png` | 1024×1024 |
| 吧台桌高脚椅组/吧台桌（遮罩蒙版）.png | `Things/Building/Restaurant/BarCounter/RSR_BarCounter_Atlas_m.png` | 1024×1024 |
| 吧台桌高脚椅组/吧台桌（图标）.png | `UI/Icons/Buildings/Restaurant/RSR_BarCounter.png` | 256×256 |
| 吧台桌高脚椅组/吧台桌（图标）（遮罩蒙版）.png | `UI/Icons/Buildings/Restaurant/RSR_BarCounter_m.png` | 256×256 |
| 旋转寿司台组/旋转寿司台（原版）.png | `Things/Building/Restaurant/SushiConveyor/RSR_SushiConveyor_Atlas.png` | 1024×1024 |
| 旋转寿司台组/旋转寿司台（遮罩蒙版）.png | `Things/Building/Restaurant/SushiConveyor/RSR_SushiConveyor_Atlas_m.png` | 1024×1024 |
| 旋转寿司台组/旋转寿司台（无表面传送带款）.png | `Things/Building/Restaurant/SushiConveyor/RSR_SushiConveyor_BaseAtlas.png` | 1024×1024 |
| 旋转寿司台组/旋转寿司台（传送带区域蒙版）.png | `Things/Building/Restaurant/SushiConveyor/Masks/RSR_SushiConveyor_BeltRegion.png` | 1024×1024 |
| 旋转寿司台组/旋转寿司台（图标）.png | `UI/Icons/Buildings/Restaurant/RSR_SushiConveyor.png` | 256×256 |
| 旋转寿司台组/旋转寿司台（图标）（遮罩蒙版）.png | `UI/Icons/Buildings/Restaurant/RSR_SushiConveyor_m.png` | 256×256 |
| 旋转寿司台组/传送带动画1.png | `Things/Building/Restaurant/SushiConveyor/Animation/RSR_SushiConveyor_Belt_Frame01.png` | 512×512 |
| 旋转寿司台组/传送带动画2.png | `Things/Building/Restaurant/SushiConveyor/Animation/RSR_SushiConveyor_Belt_Frame02.png` | 512×512 |
| 旋转寿司台组/传送带动画3.png | `Things/Building/Restaurant/SushiConveyor/Animation/RSR_SushiConveyor_Belt_Frame03.png` | 512×512 |

## 文件核对

共 61 张 PNG：42 张 256×256、13 张 512×512、6 张 1024×1024。所有文件与原压缩包提取内容的 SHA-256 一致，普通染色蒙版均能找到同尺寸主图。
