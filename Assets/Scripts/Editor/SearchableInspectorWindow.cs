using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.IO;
using UnityEditorInternal;


/// <summary>
/// プロパティを検索可能なインスペクタ
/// </summary>
public class SearchableInspectorWindow : EditorWindow
{
	[MenuItem("Window/Searchable Inspector %i")]
	static void showWindow()
	{
		var window = GetWindow<SearchableInspectorWindow>();
		window.onEnabledFirstTiming = true;
	}

	Assembly gameAssembly;
	Assembly unityEditorAssembly;


	ActiveEditorTracker editorTracker;
	ActiveEditorTracker EditorTracker {
		get
		{
			if (editorTracker == null) {
				editorTracker = new ActiveEditorTracker();
			}
			return editorTracker;
		}
	}
	bool isLocked;

	/// <summary>
	/// プロパティをどのように表示するか
	/// </summary>
	class ShowPropertyInfo
	{
		public ShowPropertyInfo(SerializedProperty property, bool showChildren)
		{
			this.propery = property;
			this.showAllChildren = showChildren;
		}

		public SerializedProperty propery;

		/// <summary>
		/// 全ての子プロパティを表示するか
		/// </summary>
		public bool showAllChildren;
	}

	/// <summary>
	/// どのようにインスペクタを表示するかの情報
	/// コンポーネント単体の描画に使用
	/// </summary>
	class ShowInspectorInfo
	{
		public ShowInspectorInfo(Editor inspectorEditor, bool isShowCompontnt, bool isFoldout, List<ShowPropertyInfo> showProperties)
		{
			this.inspectorEditor = inspectorEditor;
			this.isShowCompontnt = isShowCompontnt;
			this.isFoldout = isFoldout;
			this.showProperties = showProperties;
		}
		/// <summary>
		/// 使用するEditor
		/// </summary>
		public Editor inspectorEditor;

		/// <summary>
		/// コンポーネント自体を表示するなら true
		/// このフラグが false なら、コンポーネントのタイトルバー自体も非表示にする
		/// </summary>
		public bool isShowCompontnt;

		/// <summary>
		/// コンポーネントが折り畳まれているなら true
		/// こちらは、タイトルバーは表示する
		/// </summary>
		public bool isFoldout;

		/// <summary>
		/// 実際に表示するプロパティ
		/// フィルタリングされて、全てのプロパティは表示しないかもしれない
		/// </summary>
		public List<ShowPropertyInfo> showProperties { get; set; }
	}

	/// <summary>
	/// インスペクタ全体の描画に使用
	/// </summary>
	List<ShowInspectorInfo> showInspectorEditors = new List<ShowInspectorInfo>();

	Vector2 scrollPosition;
	Dictionary<Editor, ShowInspectorInfo> activeEditorTable = new Dictionary<Editor, ShowInspectorInfo>();

	/// <summary>
	/// Window が有効になってから、GUI を初めて更新するまでの間 true
	/// </summary>
	bool onEnabledFirstTiming;

	/// <summary>
	/// プロパティハンドラを取得するための変数
	/// </summary>
	private MethodInfo getHandlerMethodInfo;
	private static object[] getHandlerParams;
	private object handler;

	/// <summary>
	/// プロパティドローワーの情報を取得するための変数
	/// </summary>
	private PropertyInfo propertyDrawerInfo;
	private MethodInfo guiHandler;
	private object[] guiParams;

	/// <summary>
	/// 編集するオブジェクトに対応する、カスタムエディタを検索する
	/// </summary>
	MethodInfo findCustomEditorType;
	object[] findCustomEditorTypeArg = new object[2];

	/// <summary>
	/// UnityEditor.GenericInspector をキャッシュ
	/// </summary>
	System.Type genericInspectorType;

	/// <summary>
	/// 検索ボックスのスタイル
	/// </summary>
	GUIStyle toolbarSearchFieldStyle;
	GUIStyle toolbarSearchFieldCancelButtonStyle;
	GUIStyle toolbarSearchFieldCancelButtonEmptyStyle;

	void OnEnable()
	{
		// ウインドウタイトル変更
		titleContent = new GUIContent("Searchable Inspector");

		onEnabledFirstTiming = true;

		EditorTracker.isLocked = isLocked;
		EditorTracker.RebuildIfNecessary();

		unityEditorAssembly = Assembly.Load("UnityEditor");

		getHandlerMethodInfo = System.Type.GetType("UnityEditor.ScriptAttributeUtility, UnityEditor").GetMethod("GetHandler", BindingFlags.NonPublic | BindingFlags.Static);
		findCustomEditorType = System.Type.GetType("UnityEditor.CustomEditorAttributes, UnityEditor").GetMethod("FindCustomEditorTypeByType", BindingFlags.NonPublic | BindingFlags.Static);
		genericInspectorType = unityEditorAssembly.GetType("UnityEditor.GenericInspector");
		gameAssembly = Assembly.Load("Assembly-CSharp");

		updateCacheActiveEditors();
		buildDrawEditors(true);
	}

	void OnInspectorUpdate()
	{
		// 定期的に監視し、変化があれば表示を更新
		if (checkSelectionGameObjectEditted()) {
			Repaint();
		}
	}

	void OnDestroy()
	{
		EditorTracker.Destroy();
	}

	/// <summary>
	/// 選択中ゲームオブジェクトのコンポーネントの状態を監視するためにキャッシュ
	/// </summary>
	IEnumerable<Component> cachedSelectionGameObjectsComponents;

	/// <summary>
	/// 選択中のゲームオブジェクトの状態に何らかの変化があればtrueを返す
	/// この関数の内部でゲームオブジェクトの状態をキャッシュする
	/// </summary>
	/// <returns></returns>
	bool checkSelectionGameObjectEditted()
	{
		EditorTracker.VerifyModifiedMonoBehaviours();
		return EditorTracker.isDirty;
	}

	/// <summary>
	/// 検索文字列
	/// </summary>
	string searchText;

	private void OnGUI()
	{
		// 検索バーのスタイル取得
		if (toolbarSearchFieldStyle == null) {
			toolbarSearchFieldStyle = GetStyle("ToolbarSeachTextField");
		}
		if (toolbarSearchFieldCancelButtonStyle == null) {
			toolbarSearchFieldCancelButtonStyle = GetStyle("ToolbarSeachCancelButton");
		}
		if (toolbarSearchFieldCancelButtonEmptyStyle == null) {
			toolbarSearchFieldCancelButtonEmptyStyle = GetStyle("ToolbarSeachCancelButtonEmpty");
		}

		switch (Event.current.type) {
		case EventType.Repaint:
			EditorTracker.ClearDirty();
			break;
		case EventType.Layout: // レイアウト再計算のため、状態更新。
			if (updateCacheActiveEditors()) {
				buildDrawEditors(true);
			}
			break;
		}

		// 表示するものが無い場合 return
		if (activeEditorTable == null ||
			EditorTracker.activeEditors.Length == 0) {
			return;
		}

		// ヘッダを表示
		if (EditorTracker.activeEditors[0].target is GameObject) {
			EditorTracker.activeEditors[0].DrawHeader();
		}

		// 検索ボックスの表示
		EditorGUILayout.Separator();
		using (var changeScope = new EditorGUI.ChangeCheckScope()) {
			searchText = searchField(searchText);

			// 表示内容を更新
			buildDrawEditors(changeScope.changed);
		}
		EditorGUILayout.Separator();
		drawSeparator();

		using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
			drawEditors();
			EditorGUILayout.Separator();
			GUILayout.Space(15.0f);
			scrollPosition = scrollScope.scrollPosition;
		}

		// ドラッグアンドドロップ制御
		controlDragAndDrop();
	}

	/// <summary>
	/// プレハブアセットかどうか
	/// </summary>
	/// <returns></returns>
	bool isPrefabAsset()
	{
		if (Selection.activeGameObject &&
			PrefabUtility.GetPrefabAssetType(Selection.activeGameObject) != PrefabAssetType.NotAPrefab && !PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject)) {
			return true;
		}
		return false;
	}

	/// <summary>
	/// 描画に使用する Editor を確定
	/// 描画内容の再構築が必要なら、true を返す
	/// </summary>
	bool updateCacheActiveEditors()
	{
		// GCが高頻度で発生する事になるので、チューニング対象
		var newActiveEditorTable = new Dictionary<Editor, ShowInspectorInfo>();
		// 以前の更新時と、変化していない Editor の数
		var sameEditorCount = 0;
		foreach (var editor in EditorTracker.activeEditors) {
			if (activeEditorTable.ContainsKey(editor)) {
				newActiveEditorTable.Add(editor, new ShowInspectorInfo(editor, activeEditorTable[editor].isShowCompontnt, activeEditorTable[editor].isFoldout, activeEditorTable[editor].showProperties));
				++sameEditorCount;
			} else {
				newActiveEditorTable.Add(editor, new ShowInspectorInfo(editor, true, true, new List<ShowPropertyInfo>()));
			}
		}
		activeEditorTable = newActiveEditorTable;
		return sameEditorCount != EditorTracker.activeEditors.Length;
	}

	/// <summary>
	/// インスペクタの実際の描画内容を構築
	/// </summary>
	void buildDrawEditors(bool isBuildFilteredProperties)
	{
		foreach (var editor in EditorTracker.activeEditors) {
			var isShowComponent = activeEditorTable[editor].isShowCompontnt;

			if (editor.targets.Length != Selection.objects.Length) {
				isShowComponent = false;
			} else if (!string.IsNullOrEmpty(searchText) && isBuildFilteredProperties) {
				var showProperties = new List<ShowPropertyInfo>();
				isShowComponent = buildDrawEditor(editor, showProperties);
				activeEditorTable[editor].showProperties = showProperties;
			} else if (string.IsNullOrEmpty(searchText)) {
				isShowComponent = true; // 検索ボクスに何も入力されていないなら、表示確定
			}
			activeEditorTable[editor].isShowCompontnt = isShowComponent;
		}
	}

	/// <summary>
	/// ドラッグアンドドロップ制御
	/// </summary>
	void controlDragAndDrop()
	{
		if (Event.current.type == EventType.DragUpdated ||
			Event.current.type == EventType.DragPerform) {

			DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;

			foreach (var item in DragAndDrop.objectReferences) {
				if (item is MonoScript) {
					DragAndDrop.visualMode = DragAndDropVisualMode.Link;
					break;
				}
			}

			if (DragAndDrop.visualMode != DragAndDropVisualMode.Link) {
				return;
			}

			if (Event.current.type == EventType.DragPerform) {
				DragAndDrop.AcceptDrag();

				foreach (var item in DragAndDrop.paths) {
					var filename = Path.GetFileNameWithoutExtension(item);

					var addType = gameAssembly.GetType(filename);

					foreach (GameObject selectObject in Selection.gameObjects) {
						Undo.AddComponent(selectObject, addType);
					}
				}
			}
		}
	}

	/// <summary>
	/// 区切り線の描画
	/// </summary>
	void drawSeparator()
	{
		var lineStyle = new GUIStyle("box");
		lineStyle.border.top = lineStyle.border.bottom = 1;
		lineStyle.margin.top = lineStyle.margin.bottom = 1;
		lineStyle.padding.top = lineStyle.padding.bottom = 1;
		lineStyle.margin.left = lineStyle.margin.right = 0;
		lineStyle.padding.left = lineStyle.padding.right = 0;
		GUILayout.Box(GUIContent.none, lineStyle, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
	}

	/// <summary>
	/// コンポーネントリストを表示
	/// </summary>
	void drawEditors()
	{
		int count = 0;
		var isHideSomeComponent = false; // 一つ以上のコンポーネントが非表示
		foreach (var editor in EditorTracker.activeEditors) {
			if (editor == null ||
				editor.target == null ||
				editor.target.GetType() == typeof(AssetImporter) ||
				editor.target is GameObject) {
				continue;
			}

			if (Event.current.type == EventType.Repaint) {
				typeof(Editor).GetProperty("isInspectorDirty", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, false);
			}

			var foldout = activeEditorTable[editor].isFoldout;
			var isShowComponent = activeEditorTable[editor].isShowCompontnt;
			var showProperties = activeEditorTable[editor].showProperties;

			if (!!isShowComponent) {
				if (!hasLargeHeader(editor)) {
					activeEditorTable[editor].isFoldout = EditorGUILayout.InspectorTitlebar(activeEditorTable[editor].isFoldout, editor);
				} else {
					editor.DrawHeader();
				}

				bool isEnabled = (bool)editor.GetType().GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);

				using (var diableScope = new EditorGUI.DisabledGroupScope(!isEnabled)) {
					EditorGUIUtility.labelWidth = Screen.width * 0.4f; // 0.4は調整値
					if (!!activeEditorTable[editor].isFoldout) {
						if (string.IsNullOrEmpty(searchText)) {
							drawFullInspector(editor);
						} else {
							var componentSerializedObject = editor.serializedObject;
							componentSerializedObject.Update();
							drawProperties(showProperties);
							componentSerializedObject.ApplyModifiedProperties();
						}
					}
				}

				++count;
			} else {
				isHideSomeComponent = true;
			}
		}

		if (!!isHideSomeComponent) {
			drawSeparator();
			EditorGUILayout.HelpBox("Several components are hidden.", MessageType.Info);
		}
	}

	/// <summary>
	/// インスペクタ表示
	/// </summary>
	/// <param name="editor"></param>
	void drawFullInspector(Editor editor)
	{
		++EditorGUI.indentLevel;
		editor.OnInspectorGUI();
		--EditorGUI.indentLevel;
	}

	/// <summary>
	/// ヘッダを表示するか
	/// </summary>
	/// <param name="editor"></param>
	/// <returns></returns>
	bool hasLargeHeader(Editor editor)
	{
		if (AssetDatabase.IsMainAsset(editor.target) || AssetDatabase.IsSubAsset(editor.target) ||
			editor.target is GameObject ||
			editor.target is Material ||
			editor == EditorTracker.activeEditors[0]) {
			return true;
		}
		return false;
	}

	/// <summary>
	/// コンポーネント単体を表示
	/// </summary>
	/// <param name="componentEditor">表示するコンポーネントのEditor</param>
	bool buildDrawEditor(Editor componentEditor, List<ShowPropertyInfo> outViewProperties)
	{
		// 検索ボックスに入力されたテキストに応じて、プロパティをフィルタリングして表示
		if ((componentEditor.GetType() == genericInspectorType && getCustomEditorType(componentEditor, false) == null) ||
			getCustomEditorType(componentEditor, true) != null ||
			componentEditor.targets.Length == 1) {
			var propertyIterator = componentEditor.serializedObject.GetIterator();
			if (propertyIterator.NextVisible(true)) {
				buildFileredProperties(propertyIterator, outViewProperties);
			}
		} else {
			GUILayout.Label("Multi-object editing not supported.", EditorStyles.helpBox);
		}

		return outViewProperties.Count > 0;
	}

	/// <summary>
	/// 検索文字列によるフィルタが適用されたプロパティのリストを返す
	/// </summary>
	/// <param name="iterator">プロパティのイテレータ。走査する先頭のプロパティを渡す</param>
	/// <param name="outViewProperties"></param>
	/// <returns>一つでも表示するプロパティがあるなら true</returns>
	bool buildFileredProperties(SerializedProperty iterator, List<ShowPropertyInfo> outViewProperties)
	{
		var oldDepth = iterator.depth;

		var viewCount = 0;

		do {
			var handler = getHandlerMethodInfo.Invoke(null, new object[1] { iterator });
			var type = handler.GetType();
			propertyDrawerInfo = type.GetProperty("hasPropertyDrawer", BindingFlags.Public | BindingFlags.Instance);

			var isFoldout = true;

			if (search(iterator)) {
				outViewProperties.Add(new ShowPropertyInfo(iterator.Copy(), true));
				++viewCount;
				continue;
			}

			var currentPosition = outViewProperties.Count;
			var hasPropertyDrawer = (bool)propertyDrawerInfo.GetValue(handler);
			if (!!iterator.hasVisibleChildren && isFoldout && iterator.propertyType == SerializedPropertyType.Generic && !hasPropertyDrawer) {
				var childIterator = iterator.Copy();
				childIterator.NextVisible(true);
				if (buildFileredProperties(childIterator, outViewProperties)) {
					outViewProperties.Insert(currentPosition, new ShowPropertyInfo(iterator.Copy(), false));
					++viewCount;
				}
			}
		} while (iterator.NextVisible(false) && (oldDepth == iterator.depth));
		return viewCount > 0;
	}

	/// <summary>
	/// 指定のプロパティが検索条件にヒットするなら trueを返す
	/// </summary>
	/// <param name="property"></param>
	/// <returns></returns>
	bool search(SerializedProperty property)
	{
		var searchTextTokens = searchText.Split(' ');
		foreach (var token in searchTextTokens) {
			if (string.IsNullOrEmpty(token)) {
				continue;
			}

			if (property.displayName.Replace(" ", "").IndexOf(token, System.StringComparison.CurrentCultureIgnoreCase) >= 0) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// プロパティリストを描画
	/// </summary>
	/// <param name="properties"></param>
	void drawProperties(List<ShowPropertyInfo> properties)
	{
		if (properties.Count == 0) {
			return;
		}

		int oldDepth = properties[0].propery.depth;
		var collapsedDepth = 99;
		var collapsed = false;
		var defaultIndentLevel = EditorGUI.indentLevel;
		var defaultColor = GUI.color;
		foreach (var propertyInfo in properties) {
			// インデントを設定
			EditorGUI.indentLevel = propertyInfo.propery.depth + 1;

			if (!!collapsed && propertyInfo.propery.depth <= collapsedDepth) {
				collapsed = false;
			}

			// プロパティが折り畳まれているなら、それより下位のプロパティは非表示にする
			if (!collapsed) {
				if (!EditorGUILayout.PropertyField(propertyInfo.propery, propertyInfo.showAllChildren)) {
					collapsedDepth = propertyInfo.propery.depth;
					collapsed = true;
				}
			}

			// ヒットしたプロパティに目印をつける
			if (!!propertyInfo.showAllChildren) {
				GUI.color = Color.yellow;
				var lastRect = GUILayoutUtility.GetLastRect();
				lastRect.x = 6.0f;
				lastRect.xMax = lastRect.x + 5.0f;
				GUI.Box(lastRect, "");
				GUI.color = defaultColor;
			}

			oldDepth = propertyInfo.propery.depth;
		}

		EditorGUI.indentLevel = defaultIndentLevel;
		GUI.color = defaultColor;
	}

	/// <summary>
	/// ロックボタン
	/// </summary>
	/// <param name="rect"></param>
	void ShowButton(Rect rect)
	{
		using (var scope = new EditorGUI.ChangeCheckScope()) {
			isLocked = GUI.Toggle(rect, isLocked, GUIContent.none, "IN LockButton");
			if (!!scope.changed) {
				EditorTracker.isLocked = isLocked;
			}
		}
	}

	/// <summary>
	/// カスタムエディタの型を取得する
	/// 存在しない場合は null
	/// </summary>
	/// <param name="editor">元のエディタ</param>
	/// <param name="multiEdit">複数編集可能か</param>
	System.Type getCustomEditorType(Editor editor, bool multiEdit)
	{
		findCustomEditorTypeArg[0] = editor.target.GetType();
		findCustomEditorTypeArg[1] = multiEdit;
		return findCustomEditorType.Invoke(null, findCustomEditorTypeArg) as System.Type;
	}

	/// <summary>
	/// 検索ボックスを表示
	/// </summary>
	/// <param name="text"></param>
	/// <returns></returns>
	string searchField(string text)
	{
		if (onEnabledFirstTiming) {
			if (Event.current.type == EventType.Repaint) {
				EditorGUI.FocusTextInControl("SearchTextField");
				onEnabledFirstTiming = false;
			}
		}

		using (var scope = new EditorGUILayout.HorizontalScope()) {
			GUILayout.Space(8.0f); // 位置微調整
			var labelRect = EditorStyles.label.CalcSize(new GUIContent("Filter "));
			EditorGUILayout.LabelField("Filter ", GUILayout.Width(labelRect.x));

			GUI.SetNextControlName("SearchTextField");
			text = EditorGUILayout.TextField(text, toolbarSearchFieldStyle);
			if (text == "") {
				GUILayout.Button(GUIContent.none, toolbarSearchFieldCancelButtonEmptyStyle);
			} else {
				if (GUILayout.Button(GUIContent.none, toolbarSearchFieldCancelButtonStyle)) {
					text = "";
					GUIUtility.keyboardControl = 0;
				}
			}

			GUILayout.Space(8.0f); // 位置微調整
		}

		return text;
	}

	/// <summary>
	/// スタイル取得用、ヘルパ
	/// </summary>
	/// <param name="styleName"></param>
	/// <returns></returns>
	static private GUIStyle GetStyle(string styleName)
	{
		GUIStyle gUIStyle = GUI.skin.FindStyle(styleName);
		if (gUIStyle == null) {
			gUIStyle = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle(styleName);
		}
		if (gUIStyle == null) {
			Debug.LogError("Missing built-in guistyle " + styleName);
			gUIStyle = new GUIStyle();
		}
		return gUIStyle;
	}
}
