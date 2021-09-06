using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Map;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.Utilities;
using UnityEngine;

namespace Mapbox.Unity
{
	public class TaskLevel
	{


	}

	public class TaskManager
	{
		public Action<TaskWrapper> TaskStarted = (t) => { };
		public Action<CanonicalTileId> TaskCancelled = (t) => { };
		public int ActiveTaskLimit = 3;
		protected HashSet<TaskWrapper> _runningTasks;

		private Dictionary<int, TaskWrapper> _allTasks;
		public Queue<int> _taskQueue;

		// protected Dictionary<int, TaskWrapper> _tasksInQueue;
		// protected Queue<int> _taskQueue;
		protected Dictionary<CanonicalTileId, HashSet<int>> _tasksByTile = new Dictionary<CanonicalTileId, HashSet<int>>();
		//protected PriorityQueue<TaskWrapper, int> _taskPriorityQueue;

		private static object _lock = new object();

		public TaskManager()
		{
			_runningTasks = new HashSet<TaskWrapper>();
			_taskQueue = new Queue<int>();
			_allTasks = new Dictionary<int, TaskWrapper>();

			_tasksByTile = new Dictionary<CanonicalTileId, HashSet<int>>();
			//_taskPriorityQueue = new PriorityQueue<TaskWrapper, int>();
			Runnable.Run(UpdateTaskManager());
		}

		public IEnumerator UpdateTaskManager()
		{
			while (true)
			{
				while (_taskQueue.Count > 0 && _runningTasks.Count < ActiveTaskLimit)
				{
					var firstPeek = _taskQueue.Peek();
					if (_allTasks.ContainsKey(firstPeek) &&
					    _allTasks[firstPeek].EnqueueFrame > Time.frameCount - 15)
					{
						yield return null;
					}
					else
					{
						var wrapperId = _taskQueue.Dequeue();
						TaskWrapper wrapper;
						if (!_allTasks.ContainsKey(wrapperId))
						{
							continue;
						}
						else
						{
							wrapper = _allTasks[wrapperId];
							_allTasks.Remove(wrapperId);
							_tasksByTile[wrapper.TileId].Remove(wrapperId);
							if (_tasksByTile[wrapper.TileId].Count == 0)
							{
								_tasksByTile.Remove(wrapper.TileId);
							}
						}

						var task = Task.Run(wrapper.Action);
						_runningTasks.Add(wrapper);
						task.ContinueWith((t) => { ContinueWrapper(t, wrapper); }, TaskScheduler.FromCurrentSynchronizationContext());
						TaskStarted(wrapper);
					}
				}

				yield return null;
			}
		}

		private void ContinueWrapper(Task task, TaskWrapper taskWrapper)
		{
			_runningTasks.Remove(taskWrapper);
			//taskWrapper.Finished(taskWrapper);
			if (taskWrapper.ContinueWith != null)
			{
				taskWrapper.ContinueWith(task);
			}
		}

		public void AddTask(TaskWrapper taskWrapper, int priorityLevel = 3)
		{
			lock (_lock)
			{
				if (taskWrapper != null)
				{
					if (!_allTasks.ContainsKey(taskWrapper.Id))
					{
						taskWrapper.EnqueueFrame = Time.frameCount;
						_allTasks.Add(taskWrapper.Id, taskWrapper);

						if (!_tasksByTile.ContainsKey(taskWrapper.TileId))
						{
							_tasksByTile.Add(taskWrapper.TileId, new HashSet<int>());
						}
						_tasksByTile[taskWrapper.TileId].Add(taskWrapper.Id);
						_taskQueue.Enqueue(taskWrapper.Id);
					}
					else
					{
						_allTasks.Remove(taskWrapper.Id);
						_tasksByTile[taskWrapper.TileId].Remove(taskWrapper.Id);
						if (_tasksByTile[taskWrapper.TileId].Count == 0)
						{
							_tasksByTile.Remove(taskWrapper.TileId);
						}


						taskWrapper.EnqueueFrame = Time.frameCount;
						_allTasks.Add(taskWrapper.Id, taskWrapper);

						if (!_tasksByTile.ContainsKey(taskWrapper.TileId))
						{
							_tasksByTile.Add(taskWrapper.TileId, new HashSet<int>());
						}
						_tasksByTile[taskWrapper.TileId].Add(taskWrapper.Id);
						_taskQueue.Enqueue(taskWrapper.Id);
					}
				}
			}

			//_taskPriorityQueue.Enqueue(taskWrapper, priority);
		}

		public void CancelTile(CanonicalTileId tileId)
		{
			if (_tasksByTile.ContainsKey(tileId))
			{
				foreach (var taskId in _tasksByTile[tileId])
				{
					if (_allTasks.ContainsKey(taskId))
					{
						var task = _allTasks[taskId];
						TaskCancelled(tileId);
						_allTasks.Remove(taskId);
						task.OnCancelled?.Invoke();
					}
				}

				_tasksByTile.Remove(tileId);
			}
		}
	}

	public class TaskWrapper
	{
		public int Id;

		public int EnqueueFrame;
		// public Action<TaskWrapper> Cancelled = (t) => { };
		// public Action<TaskWrapper> Finished = (t) => { };
		public CanonicalTileId TileId;
		public Action Action;
		public CancellationTokenSource Token;
		public Action<Task> ContinueWith;
		public Action OnCancelled;

		public TaskWrapper(int id)
		{
			Id = id;
		}

#if UNITY_EDITOR
		public string Info;
#endif
	}

	public class EditorTaskManager : TaskManager
	{
		public int ActiveTaskCount => _runningTasks.Count;
		public int TaskQueueSize => _taskQueue.Count; //_taskQueue.Count;
		public int TasksInQueue => _taskQueue.Count;
	}
}