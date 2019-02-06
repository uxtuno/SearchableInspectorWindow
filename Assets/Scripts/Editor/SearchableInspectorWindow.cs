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

	/// <summary>
	/// 表示中 Editor の状態を保持するためのクラス
	/// </summary>
	struct EditorInfo
	{
		public EditorInfo(Editor editor, bool foldout)
		{
			this.editor = editor;
			this.foldout = foldout;
			this.serializedObject = new SerializedObject(editor.targets);
		}

		public Editor editor;
		public bool foldout;
		public SerializedObject serializedObject;
	}

	Vector2 scrollPosition;
	List<EditorInfo> editors = new List<EditorInfo>();

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

		Selection.selectionChanged += () => {
			rebuildEditorElements();
		};

		EditorApplication.update += observeContentsChanged;
		onEnabledFirstTiming = true;
	}

	void OnDisable()
	{
		EditorApplication.update -= observeContentsChanged;
	}

	/// <summary>
	/// 表示内容の変化を検知
	/// </summary>
	void observeContentsChanged()
	{
		// 定期的に監視し、変化があれば表示を更新
		if (checkSelectionGameObjectEditted()) {
			rebuildEditorElements();
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
		IEnumerable<Component> selectionGameObjectsComponents = new Component[0];
		foreach (var item in Selection.gameObjects) {
			selectionGameObjectsComponents = selectionGameObjectsComponents.Concat(item.GetComponents<Component>());
		}

		var result = false;
		if (cachedSelectionGameObjectsComponents != null && cachedSelectionGameObjectsComponents.SequenceEqual(selectionGameObjectsComponents)) {
			result = false;
		}
		else {
			result = true;
		}

		cachedSelectionGameObjectsComponents = selectionGameObjectsComponents;
		return result;
	}

	void rebuildEditorElements()
	{
		editors.Clear();

		// ヘッダの描画用
		selectObjectEditor = Editor.CreateEditor(Selection.objects);

		Component[] activeObjectComponents = null;
		if (!!Selection.activeGameObject) {
			activeObjectComponents = Selection.activeGameObject.GetComponents<Component>();
		}

		if (activeObjectComponents == null) {
			Repaint();
			return;
		}
		var sameTypeObjectList = new List<List<Object>>();


		for (int i = 0; i < activeObjectComponents.Length; ++i) {
			if (activeObjectComponents[i] == null) {
				continue;
			}

			var typeSameIndex = activeObjectComponents
				.Where(item => item != null && item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
				.Select((item, index) => new { item, index }) // インデックスを取得可能にする
				.First(item => item.item == activeObjectComponents[i]).index; // インデックス取得

			sameTypeObjectList.Add(new List<Object>());
			sameTypeObjectList[sameTypeObjectList.Count - 1].Add(activeObjectComponents[i]);

			foreach (var selectObject in Selection.gameObjects) {
				if (selectObject == Selection.activeGameObject) {
					continue;
				}

				var components = selectObject.GetComponents<Component>();

				var sameComponent = components
					.Where(item => item != null && item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
					.Select((item, index) => new { item, index }) // インデックスを取得可能にする
					.Where(item => item.index == typeSameIndex) // 同一型のコンポーネントのリストから特定インデックスの要素を取り出す
					.FirstOrDefault()?.item; // 単一要素として取得

				if (!!sameComponent) {
					sameTypeObjectList[i].Add(sameComponent);
				}
			}
		}

		foreach (var components in sameTypeObjectList.Where(components => components.Count == Selection.gameObjects.Length)) {
			var editorInfo = new EditorInfo(Editor.CreateEditor(components.ToArray()), true);
			editors.Add(editorInfo);
		}

		Repaint();
	}

	string searchText;

	private void OnGUI()
	{
		EditorGUIUtility.labelWidth = 0.0f;

		if (!!selectObjectEditor) {
			selectObjectEditor.DrawHeader();
		}

		// 表示するものが無い場合 return
		if (editors == null || !selectObjectEditor) {
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

		if (!string.IsNullOrEmpty(searchText)) {
			using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
				for (int i = 0; i < editors.Count; ++i) {
					var editor = editors[i].editor;

					if (editor.target == null) {
						continue;
					}

					var foldout = EditorGUILayout.InspectorTitlebar(editors[i].foldout, editor);

					++EditorGUI.indentLevel;
					if (!!foldout) {
						EditorGUIUtility.labelWidth = 0;
						editors[i].serializedObject.Update();

						var iterator = editors[i].serializedObject.GetIterator();
						iterator.Next(true);
						while (iterator.NextVisible(false)) {
							if (iterator.name.IndexOf(searchText, System.StringComparison.CurrentCultureIgnoreCase) >= 0) {
								EditorGUILayout.PropertyField(iterator, true);
							}
						}
						editors[i].serializedObject.ApplyModifiedProperties();
					}

					--EditorGUI.indentLevel;

					editors[i] = new EditorInfo(editor, foldout);
				}
				scrollPosition = scrollScope.scrollPosition;
			}
			return;
		}

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

	void drawComponents()
	{
		for (int i = 0; i < editors.Count; ++i) {
			var editor = editors[i].editor;

			if (editor.target == null) {
				continue;
			}

			using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
				var foldout = EditorGUILayout.InspectorTitlebar(editors[i].foldout, editor);

				++EditorGUI.indentLevel;
				if (!!foldout) {
					if (editor.GetType().CustomAttributes.Any(attribute => attribute.AttributeType == typeof(CanEditMultipleObjects))) {
						editor.OnInspectorGUI();
					}
					else if (editor.targets.Length == 1) {
						editor.OnInspectorGUI();
					}
					else {
						EditorGUILayout.HelpBox("Multi-object editing not supported.", MessageType.Info);
					}
				}
				--EditorGUI.indentLevel;

				editors[i] = new EditorInfo(editor, foldout);
			}
		}
	}
}
