﻿using System.Collections;
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

	void OnEnable()
	{
		// ウインドウタイトル変更
		titleContent = new GUIContent("Searchable Inspector");

		onEnabledFirstTiming = true;

		editorTracker = new ActiveEditorTracker();
		editorTracker.isLocked = isLocked;
		editorTracker.RebuildIfNecessary();

        gameAssembly = Assembly.Load("Assembly-CSharp");
        unityEditorAssembly = Assembly.Load("UnityEditor");
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
		if (editorTracker != null && editorTracker.activeEditors != null) {
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
        switch (Event.current.type) {
            case EventType.Repaint:
                editorTracker.ClearDirty();
                break;
            case EventType.Layout: // レイアウト再計算のため、状態更新。
                rebuildEditorElements();
                break;
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

		EditorGUILayout.Space();
		drawSeparator();

		using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
			drawComponents();

			EditorGUILayout.Separator();

			Component[] activeObjectComponents = null;
			if (!!Selection.activeGameObject) {
				activeObjectComponents = Selection.activeGameObject.GetComponents<Component>();
			}

			//EditorGUILayout.HelpBox("Components that are only on same of the selected objects cannot be multi-edited.", MessageType.None);

			drawSeparator();
			EditorGUILayout.Space();

			scrollPosition = scrollScope.scrollPosition;
		}

		if (Event.current.type == EventType.DragUpdated ||
			Event.current.type == EventType.DragPerform) {

			DragAndDrop.visualMode = DragAndDropVisualMode.Link;

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
	void drawComponents()
	{
        var temp = new Dictionary<Editor, EditorInfo>();
		int count = 0;
		foreach (var editor in editorTracker.activeEditors) {
            if (editor == null ||
				editor.target == null ||
				editor.target is GameObject) {
				continue;
			}

			++count;

			var foldout = EditorGUILayout.InspectorTitlebar(activeEditorTable[editor].foldout, editor);

			++EditorGUI.indentLevel;
			if (!!foldout) {
				EditorGUIUtility.labelWidth = Screen.width * 0.4f; // 0.4は調整値
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
        if (Event.current.type == EventType.Repaint) {
            typeof(Editor).GetProperty("isInspectorDirty", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(componentEditor, false);
        }
        
        // 検索ボックスに何も入力していない時のコンポーネント表示
        if (string.IsNullOrEmpty(searchText)) {
			if (componentEditor.GetType().CustomAttributes.Any(attribute => attribute.AttributeType == typeof(CanEditMultipleObjects)) ||
                componentEditor.GetType() == unityEditorAssembly.GetType("UnityEditor.GenericInspector")) {
				componentEditor.OnInspectorGUI();
			}
			else if (componentEditor.targets.Length == 1) {
				componentEditor.OnInspectorGUI();
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
