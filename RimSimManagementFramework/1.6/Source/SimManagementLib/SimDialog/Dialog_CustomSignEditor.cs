using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //编辑招牌草稿，画布实时预览，保存时才写入建筑。
    public partial class Dialog_CustomSignEditor : Window
    {
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f);
        private static readonly Color Accent = new Color(0.18f, 0.69f, 0.87f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f);
        private readonly ThingComp_CustomSign sign;
        private readonly SignFaceData southDraft;
        private readonly SignFaceData eastDraft;
        private readonly SignFaceData northDraft;
        private readonly Dictionary<string, string> numericBuffers = new Dictionary<string, string>();
        private SignFaceKind selectedFace;
        private int selectedLayerIndex = -1;
        private bool previewWest;
        private bool lockAspect = true;
        private bool showPosition;
        private Vector2 layerScroll;
        private Vector2 parameterScroll;
        private int dragControl;
        private SignImageLayerData draggedLayer;
        private Vector2 dragStart;
        private Vector2 originalOffset;

        private static float ControlHeight => Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 10f);
        private Thing SignThing => sign.parent;
        private bool FixedFacing => !SignThing.def.rotatable;
        private SignFaceData CurrentFace => GetFace(selectedFace);
        private SignImageLayerData SelectedLayer => selectedLayerIndex >= 0 && selectedLayerIndex < CurrentFace.layers.Count
            ? CurrentFace.layers[selectedLayerIndex] : null;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1200f, UI.screenWidth - 48f), Mathf.Min(820f, UI.screenHeight - 48f));

        //从建筑实际朝向打开对应编辑面，避免默认放置朝向与预览朝向不一致。
        public Dialog_CustomSignEditor(ThingComp_CustomSign sign)
        {
            this.sign = sign;
            southDraft = sign.SouthFace.Clone();
            eastDraft = sign.EastFace.Clone();
            northDraft = sign.NorthFace.Clone();
            selectedFace = FaceForRotation(SignThing.Rotation);
            previewWest = SignThing.Rotation == Rot4.West;
            SelectLayer(CurrentFace.layers.Count > 0 ? 0 : -1);
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
            draggable = true;
        }

        //先保留标题与底部操作区，再分配预览画布和可滚动的编辑栏。
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                float top = ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSMF.CustomSign.EditorTitle"),
                    SimTranslation.T("RSMF.CustomSign.EditorDescription"), true);
                float h = ControlHeight;
                float footerHeight = h * 2f + 6f;
                Rect body = new Rect(rect.x, top + 6f, rect.width, Mathf.Max(0f, rect.yMax - footerHeight - top - 18f));
                float sideWidth = Mathf.Min(360f, body.width * 0.46f);
                DrawPreview(new Rect(body.x, body.y, body.width - sideWidth - 12f, body.height));
                DrawSidebar(new Rect(body.xMax - sideWidth, body.y, sideWidth, body.height));
                DrawFooter(new Rect(rect.x, rect.yMax - footerHeight, rect.width, footerHeight));
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        //导入入口集中放在底部，保存与取消始终保持可见。
        private void DrawFooter(Rect rect)
        {
            float h = ControlHeight;
            float half = (rect.width - 8f) / 2f;
            bool canAdd = CurrentFace.layers.Count < SignImageLibrary.MaxFaceLayerCount;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x, rect.y, half, h), SimTranslation.T("RSMF.CustomSign.AddGalleryImage"), canAdd))
                Find.WindowStack.Add(new Dialog_SignImageBrowser(AddImageLayer));
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + half + 8f, rect.y, half, h), SimTranslation.T("RSMF.CustomSign.ImportPath"), canAdd))
                Find.WindowStack.Add(new Dialog_SignImagePathImport(record => AddImageLayer(record.imageId)));
            float width = Mathf.Min(160f, half);
            float y = rect.yMax - h;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x, y, width, h), SimTranslation.T("RSMF.Common.Cancel"))) Close();
            if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - width, y, width, h), SimTranslation.T("RSMF.CustomSign.SaveClose")))
            {
                sign.SetFaces(southDraft.Clone(), eastDraft.Clone(), northDraft.Clone());
                Messages.Message(SimTranslation.T("RSMF.CustomSign.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
                Close();
            }
        }

        //切换图层时清空数字输入缓冲，避免旧值覆盖新选择。
        private void SelectLayer(int index)
        {
            selectedLayerIndex = index;
            numericBuffers.Clear();
            parameterScroll = Vector2.zero;
        }

        //添加后立即选择新图层，并滚动列表使它可见。
        private void AddImageLayer(string imageId)
        {
            if (CurrentFace.layers.Count >= SignImageLibrary.MaxFaceLayerCount)
            {
                Messages.Message(SimTranslation.T("RSMF.CustomSign.MaxLayerReached"), MessageTypeDefOf.RejectInput, false);
                return;
            }
            CurrentFace.layers.Add(new SignImageLayerData
            {
                imageId = imageId, label = SignImageLibrary.GetRecord(imageId)?.label ?? imageId,
                drawOrder = CurrentFace.layers.Count
            });
            SelectLayer(CurrentFace.layers.Count - 1);
            layerScroll = Vector2.zero;
        }

        //替换当前图片但保留其位置、缩放与显示顺序。
        private void ReplaceImage(SignImageLayerData layer)
        {
            Find.WindowStack.Add(new Dialog_SignImageBrowser(imageId =>
            {
                layer.imageId = imageId;
                layer.label = SignImageLibrary.GetRecord(imageId)?.label ?? imageId;
                numericBuffers.Clear();
            }));
        }

        //删除所选图层后选择相邻图层，并重新编号。
        private void DeleteLayer()
        {
            if (SelectedLayer == null) return;
            CurrentFace.layers.RemoveAt(selectedLayerIndex);
            SelectLayer(Mathf.Min(selectedLayerIndex, CurrentFace.layers.Count - 1));
            Reorder();
        }

        //调整叠放顺序，列表顶部对应最上层。
        private void MoveLayer(int direction)
        {
            int next = selectedLayerIndex + direction;
            if (SelectedLayer == null || next < 0 || next >= CurrentFace.layers.Count) return;
            SignImageLayerData layer = SelectedLayer;
            CurrentFace.layers[selectedLayerIndex] = CurrentFace.layers[next];
            CurrentFace.layers[next] = layer;
            SelectLayer(next);
            Reorder();
        }

        //把列表顺序写入草稿，绘制与保存使用相同叠放顺序。
        private void Reorder()
        {
            for (int i = 0; i < CurrentFace.layers.Count; i++) CurrentFace.layers[i].drawOrder = i;
        }

        //获取当前编辑面的草稿。
        private SignFaceData GetFace(SignFaceKind face) => face == SignFaceKind.East ? eastDraft
            : face == SignFaceKind.North ? northDraft : southDraft;

        //西面与东面共用图层数据。
        private static SignFaceKind FaceForRotation(Rot4 rotation) => rotation.IsHorizontal ? SignFaceKind.East
            : rotation == Rot4.North ? SignFaceKind.North : SignFaceKind.South;

        //固定招牌始终采用实际朝向，其他招牌按当前页签预览。
        private Rot4 PreviewRotation() => FixedFacing ? SignThing.Rotation : selectedFace == SignFaceKind.North
            ? Rot4.North : selectedFace == SignFaceKind.East ? previewWest ? Rot4.West : Rot4.East : Rot4.South;

        //绘制当前朝向或可切换的方向按钮。
        private void DrawFaceTabs(Rect rect)
        {
            Rot4[] rotations = FixedFacing ? new[] { SignThing.Rotation } : new[] { Rot4.South, Rot4.East, Rot4.North, Rot4.West };
            float width = (rect.width - 6f * (rotations.Length - 1)) / rotations.Length;
            for (int i = 0; i < rotations.Length; i++)
            {
                Rot4 rotation = rotations[i];
                string name = rotation == Rot4.South ? "South" : rotation == Rot4.East ? "East" : rotation == Rot4.North ? "North" : "West";
                if (!SimUiStyle.DrawTabButton(new Rect(rect.x + (width + 6f) * i, rect.y, width, rect.height),
                    SimTranslation.T("RSMF.CustomSign.Face." + name), PreviewRotation() == rotation, MutedText)) continue;
                selectedFace = FaceForRotation(rotation);
                previewWest = rotation == Rot4.West;
                SelectLayer(CurrentFace.layers.Count > 0 ? 0 : -1);
                layerScroll = Vector2.zero;
            }
        }

        //关闭窗口时释放拖动捕获，避免影响其他窗口的鼠标输入。
        public override void PostClose()
        {
            if (dragControl != 0 && GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            base.PostClose();
        }
    }
}
