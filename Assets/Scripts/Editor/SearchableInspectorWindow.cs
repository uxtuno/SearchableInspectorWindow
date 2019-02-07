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

	void OnEnable()
	{
		// ウインドウタイトル変更
		titleContent = new GUIContent("Searchable Inspector");

		onEnabledFirstTiming = true;

		editorTracker = new ActiveEditorTracker();
		editorTracker.isLocked = isLocked;
		editorTracker.RebuildIfNecessary();
	}

	void OnInspectorUpdate()
	{
		// 定期的に監視し、変化があれば表示を更新
		if (checkSelectionGameObjectEditted()) {
			rebuildEditorElements();
		}
	}

	void OnDisable()
	{
		if (editorTracker != null) {
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

		Repaint();
	}

	string searchText;

	private void OnGUI()
	{
		if (Event.current.type == EventType.Repaint) {
			editorTracker.ClearDirty();
		}

		if (!!selectObjectEditor) {
			selectObjectEditor.DrawHeader();
		}

		// 表示するものが無い場合 return
		if (activeEditorTable == null ||
			editorTracker.activeEditors.Length == 0||
			!selectObjectEditor) {
			return;
		}

		// GameObject 以外のアセット類を選択した際の挙動
		if (!Selection.activeGameObject) {
			// 既定の内容を描画するのみ
			EditorGUIUtility.labelWidth = 0;

			selectObjectEditor.OnInspectorGUI();
			return;
		}

		// プレハブアセットの場合、Open Prefabボタンを表示する
		if (PrefabUtility.GetPrefabAssetType(Selection.activeGameObject) != PrefabAssetType.NotAPrefab && !PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject)) {
			selectObjectEditor.OnInspectorGUI();

			if (GUILayout.Button("Open Prefab")) {
				// プレハブ編集モードを開く
				AssetDatabase.OpenAsset(Selection.activeGameObject);
			}
			return;
		}

		if (onEnabledFirstTiming) {
			if (Event.current.type == EventType.Repaint) {
				EditorGUI.FocusTextInControl("SearchTextField");
				onEnabledFirstTiming = false;
			}
		}

		EditorGUILayout.LabelField("Search Text");
		GUI.SetNextControlName("SearchTextField");

		searchText = EditorGUILayout.TextField(searchText);

		EditorGUILayout.Separator();

		using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
			drawComponents();

			EditorGUILayout.Separator();

			Component[] activeObjectComponents = null;
			if (!!Selection.activeGameObject) {
				activeObjectComponents = Selection.activeGameObject.GetComponents<Component>();
			}

			//EditorGUILayout.HelpBox("Components that are only on same of the selected objects cannot be multi-edited.", MessageType.None);

			scrollPosition = scrollScope.scrollPosition;
		}

		if (Event.current.type == EventType.DragUpdated ||
			Event.current.type == EventType.DragPerform) {

			DragAndDrop.visualMode = DragAndDropVisualMode.Link;

			if (Event.current.type == EventType.DragPerform) {
				DragAndDrop.AcceptDrag();

				foreach (var item in DragAndDrop.paths) {
					var filename = Path.GetFileNameWithoutExtension(item);
					var addType = System.Type.GetType(filename);

					foreach (GameObject selectObject in Selection.gameObjects) {
						Undo.AddComponent(selectObject, addType);
					}
				}
			}
		}
	}

	/// <summary>
	/// コンポーネントリストを表示
	/// </summary>
	void drawComponents()
	{
		var temp = new Dictionary<Editor, EditorInfo>();
		int count = 0;
		foreach (var item in activeEditorTable.Keys) {

			var editor = item;

			if (editor == null ||
				editor.target == null ||
				editor.target is GameObject) {
				continue;
			}

			++count;

			var foldout = EditorGUILayout.InspectorTitlebar(activeEditorTable[editor].foldout, editor);

			++EditorGUI.indentLevel;
			if (!!foldout) {
				EditorGUIUtility.labelWidth = Screen.width * 0.4f;
				drawComponentInspector(editor);
			}
			--EditorGUI.indentLevel;

			temp.Add(editor, new EditorInfo(editor, foldout));
		}
		activeEditorTable = temp;
	}

	/// <summary>
	/// コンポーネント単体を表示
	/// </summary>
	/// <param name="componentEditor">表示するコンポーネントのEditor</param>
	void drawComponentInspector(Editor componentEditor)
	{
		// 検索ボックスに何も入力していない時のコンポーネント表示
		if (string.IsNullOrEmpty(searchText)) {
			if (componentEditor.GetType().CustomAttributes.Any(attribute => attribute.AttributeType == typeof(CanEditMultipleObjects))) {
				componentEditor.OnInspectorGUI();
			}
			else if (componentEditor.targets.Length == 1) {
				componentEditor.OnInspectorGUI();
			}
			else {
				EditorGUILayout.HelpBox("Multi-object editing not supported.", MessageType.Info);
			}
		} else {
			// 検索ボックスに入力されたテキストに応じて、プロパティをフィルタリングして表示
			var componentSerializedObject = activeEditorTable[componentEditor].serializedObject;
			componentSerializedObject.Update();

			var iterator = componentSerializedObject.GetIterator();
			iterator.Next(true);
			while (iterator.NextVisible(false)) {
				if (iterator.name.IndexOf(searchText, System.StringComparison.CurrentCultureIgnoreCase) >= 0) {
					EditorGUILayout.PropertyField(iterator, true);
				}
			}

			componentSerializedObject.ApplyModifiedProperties();
		}
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
}
