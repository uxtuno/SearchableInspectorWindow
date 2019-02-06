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
	[MenuItem("Window/Searchable Inspector")]
	static void showWindow()
	{
		GetWindow<SearchableInspectorWindow>();
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
	/// 選択中ゲームオブジェクト自体の Editor
	/// </summary>
	Editor gameObjectEditor;

	void OnEnable()
	{
		// ウインドウタイトル変更
		titleContent = new GUIContent("Searchable Inspector");

		Selection.selectionChanged += () => {
			rebuildEditorElements();
		};

		EditorApplication.update += observeContentsChanged;
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
		gameObjectEditor = Editor.CreateEditor(Selection.gameObjects);

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

			var typeSameIndex = activeObjectComponents
				.Where(item => item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
				.Select((item, index) => new { item, index }) // インデックスを取得可能にする
				.First(item => item.item == activeObjectComponents[i]).index; // インデックス取得

			sameTypeObjectList.Add(new List<Object>());
			sameTypeObjectList[i].Add(activeObjectComponents[i]);

			foreach (var selectObject in Selection.gameObjects) {
				if (selectObject == Selection.activeGameObject) {
					continue;
				}

				var components = selectObject.GetComponents<Component>();

				var sameComponent = components
					.Where(item => item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
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
		if (!Selection.activeGameObject) {
			return;
		}

		if (editors == null) {
			return;
		}

		if (!!gameObjectEditor) {
			gameObjectEditor.DrawHeader();
		}

		EditorGUILayout.LabelField("Search Text");
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
					if (!!foldout) {
						var iterator = editors[i].serializedObject.GetIterator();
						iterator.Next(true);
						while (iterator.NextVisible(false)) {
							if (iterator.name.IndexOf(searchText, System.StringComparison.CurrentCultureIgnoreCase) >= 0) {
								EditorGUILayout.PropertyField(iterator, true);
							}
						}
					}

					editors[i] = new EditorInfo(editor, foldout);
				}
				scrollPosition = scrollScope.scrollPosition;
			}

			Repaint();

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

			DragAndDrop.visualMode = DragAndDropVisualMode.Move;

			if (Event.current.type == EventType.DragPerform) {
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

				editors[i] = new EditorInfo(editor, foldout);
			}
		}
	}
}
