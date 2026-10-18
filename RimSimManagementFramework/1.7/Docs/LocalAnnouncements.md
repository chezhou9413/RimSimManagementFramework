# 本地公告

公告完全读取已加载的 `SimManagementLib.SimDef.AnnouncementDef`，不再请求公告服务器。
默认配置位于 `1.6/Defs/Reusable/AnnouncementDef/Announcements_Default.xml`。

## 配置

```xml
<SimManagementLib.SimDef.AnnouncementDef>
  <defName>MyMod_Announcement_Example</defName>
  <label>公告标题</label>
  <description>公告正文，可以直接编写中文。</description>
  <enabled>true</enabled>
  <revision>1</revision>
  <order>100</order>
  <publishedAt>2026-09-27T00:00:00+08:00</publishedAt>
</SimManagementLib.SimDef.AnnouncementDef>
```

- `defName` 是稳定标识，不要用于表达每次更新的版本号。
- `revision` 使用正整数；正文更新后增加版本号，会再次提醒已读玩家。
- `popupOnce=true` 使用永久单次提醒；改变 revision 不会再次弹出。要发布另一条公告时使用新的 defName。
- `mainMenuOnly=true` 只在主菜单自动展示，不会在经营管理或手动检查时打断游戏。
- `workshopUrl` 配置 Steam 工坊地址，窗口底部显示“立即订阅 · 前往 Steam”；点击打开页面，由玩家完成订阅。
- `screenshotPaths` 配置本地纹理路径列表，不含 Textures 前缀和文件扩展名。窗口提供大图和可点击缩略图。
- `enabled=false` 停止展示该公告，不删除已有历史快照。
- `order` 越大越靠前；相同时按 ISO 发布时间倒序、defName 正序排列。
- `publishedAt` 用于显示和排序，不控制定时发布。
- 可选的 `titleKey`、`bodyKey` 指定 Keyed 翻译；省略时使用 label、description。
- 公告随正常 Def 加载，不在运行中重新读取 XML；编辑后需重启游戏。

## 展示和已读

主菜单在加载完成且没有其他普通对话框时检查一次；打开经商管理或点击“检查本地公告”也会检查。
符合当前入口的未读公告集中放入一个可翻页的窗口，不限制为最新五条。
当前公告实际打开时写入已读；关闭窗口不会把尚未翻到的公告一起标记为已读。

已读标识为 `local:defName:revision`，保存在模组设置中并跨存档共享。
永久单次公告使用 `local:defName:once`；普通关闭、游戏重启和切换存档不会再次提醒。
正文历史保留最近一百条；已读标识独立保留，因此历史裁剪不会导致旧公告再次弹出。
本地默认公告 `RSMF_Announcement_RestaurantRelease` 用于介绍官方餐厅扩展，仅主菜单展示且永久单次提醒。
正文历史保留文本快照；工坊按钮和画廊位于公告弹窗中。

## 餐厅发布公告素材

素材来源：[RimSMF:Restaurant 工坊页面](https://steamcommunity.com/sharedfiles/filedetails/?id=3807662479)。
四张图片取自该页面截图列表，以最高 1280 像素尺寸随框架打包，运行期间不下载图片。
文件位于 `1.6/Textures/UI/Announcements/RestaurantRelease`。

| 本地文件 | Steam 截图 ID | 内容 |
| --- | --- | --- |
| Dining.jpg | 47813012 | 餐厅堂食 |
| Conveyor.jpg | 47813037 | 传送带自助 |
| Overview.jpg | 47813038 | 餐厅经营界面 |
| Tutorial.jpg | 47813013 | 入门教程 |
