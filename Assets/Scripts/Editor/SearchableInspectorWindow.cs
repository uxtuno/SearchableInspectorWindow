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

	bool isDirty()
	{
		editorTracker.VerifyModifiedMonoBehaviours();
		return editorTracker.isDirty;
	}

	Assembly gameAssembly;
	Assembly unityEditorAssembly;

	ActiveEditorTracker editorTracker;
	bool isLocked;

	/// <summary>
	/// 表示中 Editor の状態を保持するためのクラス
	/// </summary>
	struct EditorInfo
	{
		public EditorInfo(Editor editor, bool foldout)
		{
			this.foldout = foldout;
			this.serializedObject = new SerializedObject(editor.targets);
		}

		public bool foldout;
		public SerializedObject serializedObject;
	}

	/// <summary>
	/// プロパティをどのように表示するか
	/// </summary>
	struct ShowPropertyInfo
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

	Vector2 scrollPosition;
	Dictionary<Editor, EditorInfo> activeEditorTable = new Dictionary<Editor, EditorInfo>();

	/// <summary>
	/// Window が有効になってから、GUI を初めて更新するまでの間 true
	/// </summary>
	bool onEnabledFirstTiming;

	/// <summary>
	/// 選択中ゲームオブジェクト自体の Editor
	/// </summary>
	Editor selectObjectEditor;

	GUIStyle lineStyle;

	private MethodInfo getHandlerMethodInfo;
	private static object[] getHandlerParams;

	private object handler;

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

		editorTracker = new ActiveEditorTracker();
		editorTracker.isLocked = isLocked;
		editorTracker.RebuildIfNecessary();

		unityEditorAssembly = Assembly.Load("UnityEditor");

		getHandlerMethodInfo = System.Type.GetType("UnityEditor.ScriptAttributeUtility, UnityEditor").GetMethod("GetHandler", BindingFlags.NonPublic | BindingFlags.Static);
		findCustomEditorType = System.Type.GetType("UnityEditor.CustomEditorAttributes, UnityEditor").GetMethod("FindCustomEditorTypeByType", BindingFlags.NonPublic | BindingFlags.Static);
		genericInspectorType = unityEditorAssembly.GetType("UnityEditor.GenericInspector");
		gameAssembly = Assembly.Load("Assembly-CSharp");
	}

	void OnInspectorUpdate()
	{
		// 定期的に監視し、変化があれば表示を更新
		if (checkSelectionGameObjectEditted()) {
			Repaint();
		}
	}

	void OnDisable()
	{
		if (editorTracker != null &&
			editorTracker.activeEditors != null &&
			editorTracker.activeEditors.Length > 0 &&
			editorTracker.activeEditors[0] != null) {

			editorTracker.Destroy();
		}
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
		return isDirty();
	}

	void rebuildEditorElements()
	{
		// ヘッダの描画用
		selectObjectEditor = Editor.CreateEditor(Selection.objects);

		// GCが高頻度で発生する事になるので、チューニング対象
		var newActiveEditorTable = new Dictionary<Editor, EditorInfo>();
		foreach (var editor in editorTracker.activeEditors) {
			if (activeEditorTable.ContainsKey(editor)) {
				newActiveEditorTable.Add(editor, new EditorInfo(editor, activeEditorTable[editor].foldout));
			} else {
				newActiveEditorTable.Add(editor, new EditorInfo(editor, true));
			}
		}
		activeEditorTable = newActiveEditorTable;
	}

	string searchText;

	private void OnGUI()
	{
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
				editorTracker.ClearDirty();
				break;
			case EventType.Layout: // レイアウト再計算のため、状態更新。
				rebuildEditorElements();
				break;
		}

		// 表示するものが無い場合 return
		if (activeEditorTable == null ||
			editorTracker.activeEditors.Length == 0 ||
			!selectObjectEditor) {
			return;
		}

		// ヘッダを表示
		if (editorTracker.activeEditors[0].target is GameObject) {
			editorTracker.activeEditors[0].DrawHeader();
		}

		if (Selection.activeGameObject &&
			PrefabUtility.GetPrefabAssetType(Selection.activeGameObject) != PrefabAssetType.NotAPrefab && !PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject)) {
			editorTracker.activeEditors[0].DrawHeader();
			editorTracker.activeEditors[0].OnInspectorGUI();
			return;
		}

		EditorGUILayout.Separator();
		searchText = searchField(searchText);
		EditorGUILayout.Separator();

		drawSeparator();

		using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
			drawEditors();

			EditorGUILayout.Separator();

			Component[] activeObjectComponents = null;
			if (!!Selection.activeGameObject) {
				activeObjectComponents = Selection.activeGameObject.GetComponents<Component>();
			}

			GUILayout.Space(15.0f);

			scrollPosition = scrollScope.scrollPosition;
		}

		controlDragAndDrop();
	}

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

	void drawSeparator()
	{
		lineStyle = new GUIStyle("box");
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
		var temp = new Dictionary<Editor, EditorInfo>();
		int count = 0;
		var isHideSomeComponent = false; // 一つ以上のコンポーネントが非表示
		foreach (var editor in editorTracker.activeEditors) {
			if (editor == null ||
				editor.target == null ||
				editor.target.GetType() == typeof(AssetImporter) ||
				editor.target is GameObject) {
				continue;
			}

			typeof(EditorGUIUtility).GetMethod("ResetGUIState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

			var viewProperties = new List<ShowPropertyInfo>();

			var isShowComponent = true;
			if (editor.targets.Length != Selection.objects.Length ||
				!activeEditorTable.ContainsKey(editor)) {
				isHideSomeComponent = true;
				isShowComponent = false;
			} else if (!string.IsNullOrEmpty(searchText)) {
				isShowComponent = drawComponentInspector(editor, count, viewProperties);
			}

			var foldout = activeEditorTable[editor].foldout;

			if (!!isShowComponent) {
				if (!hasLargeHeader(editor)) {
					foldout = EditorGUILayout.InspectorTitlebar(activeEditorTable[editor].foldout, editor);
				} else {
					editor.DrawHeader();
				}

				EditorGUIUtility.labelWidth = Screen.width * 0.4f; // 0.4は調整値
				if (string.IsNullOrEmpty(searchText)) {
					drawFullInspector(editor);
				} else {
					drawProperties(viewProperties);
				}
				++count;
			} else {
				isHideSomeComponent = true;
			}

			temp.Add(editor, new EditorInfo(editor, foldout));
		}
		activeEditorTable = temp;

		drawSeparator();
		if (!!isHideSomeComponent) {
			EditorGUILayout.HelpBox("Several components are hidden.", MessageType.Info);
		}
	}

	void drawFullInspector(Editor editor)
	{
		editor.OnInspectorGUI();
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
			editor == editorTracker.activeEditors[0]) {
			return true;
		}
		return false;
	}

	/// <summary>
	/// コンポーネント単体を表示
	/// </summary>
	/// <param name="componentEditor">表示するコンポーネントのEditor</param>
	bool drawComponentInspector(Editor componentEditor, int index, List<ShowPropertyInfo> outViewProperties)
	{
		if (Event.current.type == EventType.Repaint) {
			typeof(Editor).GetProperty("isInspectorDirty", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(componentEditor, false);
		}

		// 検索ボックスに入力されたテキストに応じて、プロパティをフィルタリングして表示
		var componentSerializedObject = activeEditorTable[componentEditor].serializedObject;
		componentSerializedObject.Update();

		if ((componentEditor.GetType() == genericInspectorType && getCustomEditorType(componentEditor, false) == null) ||
			getCustomEditorType(componentEditor, true) != null ||
			componentEditor.targets.Length == 1) {
			var propertyIterator = componentSerializedObject.GetIterator();
			buildFileredProperties(propertyIterator, false, outViewProperties);
		} else {
			GUILayout.Label("Multi-object editing not supported.", EditorStyles.helpBox);
		}

		componentSerializedObject.ApplyModifiedProperties();
		return outViewProperties.Count > 0;
	}

	bool buildFileredProperties(SerializedProperty iterator, bool forceDraw, List<ShowPropertyInfo> outViewProperties)
	{
		// 一階層潜る
		if (!iterator.NextVisible(true)) {
			return false;
		}

		var oldDepth = iterator.depth;
		if (oldDepth >= 2) {
			++EditorGUI.indentLevel;
		}

		var viewCount = 0;

		do {
			var handler = getHandlerMethodInfo.Invoke(null, new object[1] { iterator });
			var type = handler.GetType();
			propertyDrawerInfo = type.GetProperty("hasPropertyDrawer", BindingFlags.Public | BindingFlags.Instance);

			bool isFoldout = true;

			if (search(iterator)) {
				outViewProperties.Add(new ShowPropertyInfo(iterator.Copy(), true));
				++viewCount;
				continue;
			}

			var currentPosition = outViewProperties.Count;
			if (!!iterator.hasVisibleChildren && isFoldout && iterator.propertyType == SerializedPropertyType.Generic) {
				var childIterator = iterator.Copy();
				if (buildFileredProperties(childIterator, false, outViewProperties)) {
					outViewProperties.Insert(currentPosition, new ShowPropertyInfo(iterator.Copy(), false));
					++viewCount;
				}
			}
		} while (iterator.NextVisible(false) && oldDepth == iterator.depth);

		if (oldDepth >= 2) {
			--EditorGUI.indentLevel;
		}

		return viewCount > 0;
	}

	bool search(SerializedProperty property)
	{
		var searchTextTokens = searchText.Split(' ');
		foreach (var token in searchTextTokens) {
			if (string.IsNullOrEmpty(token)) {
				continue;
			}

			if (property.displayName.Replace(' ', '\0').IndexOf(token, System.StringComparison.CurrentCultureIgnoreCase) >= 0) {
				return true;
			}
		}
		return false;
	}

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
			EditorGUI.indentLevel = propertyInfo.propery.depth + 1;

			if (!!collapsed && propertyInfo.propery.depth <= collapsedDepth) {
				collapsed = false;
			}

			if (!collapsed) {
				if (!EditorGUILayout.PropertyField(propertyInfo.propery, propertyInfo.showAllChildren)) {
					collapsedDepth = propertyInfo.propery.depth;
					collapsed = true;
				}
			}

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

	void ShowButton(Rect rect)
	{
		using (var scope = new EditorGUI.ChangeCheckScope()) {
			isLocked = GUI.Toggle(rect, isLocked, GUIContent.none, "IN LockButton");
			if (!!scope.changed) {
				editorTracker.isLocked = isLocked;
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
