using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;

public class MykiMazeScript : MonoBehaviour {
	public bool DebugMode = false;
	static int moduleIdCounter = 1;
	int moduleId;
	public KMSelectable[] MovementButtons;
	public GameObject CellPrefab;
	public GameObject[] VisualiserCells;
	public Transform VisualiserHolder;
	public Material[] VisualiserMats;
	public GameObject cursor;
	public KMRuleSeedable ruleSeedable;
	bool moduleSolved;
	List<Material> selectedLetters;
	string[] souvenirList;
	static Vector3Int[] Directions = new Vector3Int[]{
		new Vector3Int(1, 0, 0),
		new Vector3Int(-1, 0, 0),
		new Vector3Int(0, 1, 0),
		new Vector3Int(0, -1, 0),
		new Vector3Int(0, 0, 1),
		new Vector3Int(0, 0, -1)
	};

	struct CoordAndDirection
	{
		public Vector3Int Coords;
		public int Direction;
	}
	public class Wall
	{
		public bool Exists = true;
		public bool stateLocked = false;
	}
	public class Cell
	{
		public int Letter = -1;
		public GameObject VisualiserCellPrefab;
		public Transform Holder;
		public static GameObject[,,] VisualiserCells = new GameObject[4,4,4];
		public bool _debugmode;
		public void SetColour(Material mat)
		{
			if(_debugmode)
			{
				for(int i = 0; i < 6; i++)
				{
					VisualiserCells[Coords.x, Coords.y, Coords.z].transform.Find(i.ToString()).GetComponent<Renderer>().material = mat;
				}
			}
		}
		public void instantiate()
		{
			if(_debugmode)
			{
				VisualiserCells[Coords.x, Coords.y, Coords.z] = UnityEngine.Object.Instantiate(VisualiserCellPrefab, Holder);
				VisualiserCells[Coords.x, Coords.y, Coords.z].transform.localPosition = Coords;
			}
		}
		public bool visited = false;
		//Track which walls exist on this cell
		//+X, -X, +Y, -Y, +Z, -Z
		public Wall[] Walls = new Wall[6]{new Wall(), new Wall(), new Wall(), new Wall(), new Wall(), new Wall()};
		public int lockedWalls{
			get
			{
				int i = 0;
				foreach(Wall wall in Walls)
				{
					if(wall.stateLocked)
					{
						i++;
					}
				}
				return i;
			}
			set{}}
		public void RemoveWall(int index)
		{
			Walls[index].Exists = false;
			if(_debugmode)
			{
				VisualiserCells[Coords.x, Coords.y, Coords.z].transform.Find(index.ToString()).gameObject.SetActive(true);
			}
		}
		public List<Material> LetterMats;
		public void updateDebugCell()
		{
			if(_debugmode)
			{
				for(int i = 0; i < 6; i++)
				{
					if(Walls[i].Exists)
					{
						VisualiserCells[Coords.x, Coords.y, Coords.z].transform.Find(i.ToString()).gameObject.SetActive(false);
					}
					else
					{
						VisualiserCells[Coords.x, Coords.y, Coords.z].transform.Find(i.ToString()).gameObject.SetActive(true);
					}
					if(Letter == -1)
					{
						VisualiserCells[Coords.x, Coords.y, Coords.z].GetComponentInChildren<TextMesh>().text = "";
					}
					else
					{
						switch(Letter)
						{
							case 0:
							case 1:
							case 2:
								VisualiserCells[Coords.x, Coords.y, Coords.z].GetComponentInChildren<TextMesh>().text = $"({LetterMats[Letter].name})";
								break;
							case 3:
								VisualiserCells[Coords.x, Coords.y, Coords.z].GetComponentInChildren<TextMesh>().text = $"!{LetterMats[Letter].name}!";
								break;
							case 4:
								VisualiserCells[Coords.x, Coords.y, Coords.z].GetComponentInChildren<TextMesh>().text = LetterMats[Letter].name;
								break;
						}
						
					}
				}
			}
		}
		public Vector3Int Coords {get; set;}
	}
	Cell[][,,] Mazes = new Cell[10][,,];
	public GameObject[] Sprites;
	public GameObject LetterDisplay;
	public GameObject FarLetterDisplay;
	public Material[] ThickLetters;
	public Material[] DirectionLetters;
	GameObject[,,] displayArray = new GameObject[3, 3, 2];
	void Awake()
	{
		moduleId = moduleIdCounter++;
		foreach(KMSelectable selectable in MovementButtons)
		{
			selectable.OnInteract += delegate(){ButtonPress(Array.IndexOf(MovementButtons, selectable)); return false;};
			selectable.OnInteractEnded += delegate(){LiftButton(Array.IndexOf(MovementButtons, selectable));};
		}
		int i = 0;
		for(int j = 1; j >= 0; j--)
		{
			for(int k = 2; k >= 0; k--)
			{
				for(int l = 0; l < 3; l++)
				{
					displayArray[l, k, j] = Sprites[i];
					i++;
				}
			}
		}
		for(i = 0; i < 10; i++)
		{
			Mazes[i] = new Cell[4,4,4];
			for(int j = 0; j < 4; j++)
			{
				for(int k = 0; k < 4; k++)
				{
					for(int l = 0; l < 4; l++)
					{
						Mazes[i][j,k,l] = new Cell()
						{
							VisualiserCellPrefab = CellPrefab,
							Holder = VisualiserHolder,
							Coords = new Vector3Int(j,k,l),
							_debugmode = DebugMode
						};
					}
				}
			}
		}
		
		if(!DebugMode)
		{
			cursor.SetActive(false);
		}
	}
	Cell[,,] SelectedMaze;
	//This nested array is referred to when placing decoy letters so that they don't overwrite the other letters
	Cell[][] FreeCells;
	//These are used to uniquely pick out 3 of 5 letters for each maze
	readonly string[] combinations = new string[]{"11100", "11010", "11001", "10110", "10101", "10011", "01110", "01101", "01011", "00111"};
	//The cell originally calculated from edgework.
	Cell cellFromEdgework;
	//The cell to which the actual solution wall belongs to.
	//The cell on the other side of the wall, if applicable, is derived from this, since the player should be able to run into the solution wall from either side.
	Cell solutionCell;
	int solutionDirectionIndex;
	public KMBombInfo bombInfo;
	MonoRandom rnd;
	void Start(){
		rnd = ruleSeedable.GetRNG();
		Log($"The rule seed is {rnd.Seed}.");
		foreach(Cell cell in Mazes[0])
		{
			cell.instantiate();
		}
		for(int i = 0; i < 10; i++)
		{
			switch(rnd.Next(0, 3))
			{
				case 0:
					RecursiveBacktracking(Mazes[i]);
					//Debug.Log("Recursive Backtracking");
					break;
				case 1:
					GrowingTree(Mazes[i]);
					//Debug.Log("Growing Tree");
					break;
				case 2:
					WFC(Mazes[i]);
					//Debug.Log("WFC");
					break;
			}
			//Place the letters throughout the maze: each Cell object has an int field called "Letter". Values from 0-2 are maze identification letters, 3 is the true direction and 4 is a decoy direction.
			//The actual specific letters for these are selected and placed in later.
			List<Cell> currentFreeMazeCells = Mazes[i].Cast<Cell>().ToList();
			//Choose placement of maze identification letters
			for(int j = 0; j < 3; j++)
			{
				for(int k = 0; k < 5; k++)
				{
					int selectedIndex = rnd.Next(0, currentFreeMazeCells.Count);
					currentFreeMazeCells[selectedIndex].Letter = j;
					currentFreeMazeCells.RemoveAt(selectedIndex);
				}
			}
			//Choose placement of true direction letters
			for(int j = 0; j < 3; j++)
			{
				int selectedIndex = rnd.Next(0, currentFreeMazeCells.Count);
				currentFreeMazeCells[selectedIndex].Letter = 3;
				currentFreeMazeCells.RemoveAt(selectedIndex);
			}
			//THIS ONE SHOULD BE LEFT ON UNITYENGINE.RANDOM.RANGE AS IT IS PLACING DECOYS.
			for(int j = 0; j < 3; j++)
			{
				int selectedIndex = UnityEngine.Random.Range(0, currentFreeMazeCells.Count);
				currentFreeMazeCells[selectedIndex].Letter = 4;
				currentFreeMazeCells.RemoveAt(selectedIndex);
			}
		}
		//LEAVE ON UNITYENGINE.RANDOM.RANGE AS THIS IS RANDOMLY SELECTING 1 OF 10 MAZES
		SelectedMaze = Mazes[UnityEngine.Random.Range(0, 10)];
		//Debugging: pick a maze here
		//SelectedMaze = Mazes[0];

		//Work out the cell and choose a direction, then find the wall to run into
		cellFromEdgework = SelectedMaze[bombInfo.GetSerialNumberNumbers().First() % 4, bombInfo.GetSerialNumberNumbers().Last() % 4, bombInfo.GetBatteryCount() % 4];
		Log($"The location calculated from edgework is {cellFromEdgework.Coords}.");
		//KEEP THIS ON UNITYENGINE.RANDOM.RANGE AS IT IS CHOOSING A SOLUTION DIRECTION.
		solutionDirectionIndex = UnityEngine.Random.Range(0, 6);
		Log($"The solution direction is {new string[]{"East", "West", "Up", "Down", "North", "South"}[solutionDirectionIndex]}.");

		currentCell = cellFromEdgework;
		while(!currentCell.Walls[solutionDirectionIndex].Exists)
		{
			Vector3Int nextCoord = currentCell.Coords + Directions[solutionDirectionIndex];
			currentCell = SelectedMaze[nextCoord.x, nextCoord.y, nextCoord.z];
		}
		solutionCell = currentCell;
		//Log($"The solution wall is the {new string[]{"East", "West", "Up", "Down", "North", "South"}[solutionDirectionIndex]} wall of {solutionCell.Coords}.");
		selectedLetters = new List<Material>();
		string Markings = "";
		for(int i = 0; i < 5; i++)
		{
			if(combinations[Array.IndexOf(Mazes, SelectedMaze)][i] == "1"[0])
			{
				selectedLetters.Add(ThickLetters[i]);
				Markings += ThickLetters[i].name;
			}
		}
		
		//Add the real solution direction to selectedLetters(index 3)
		selectedLetters.Add(DirectionLetters[solutionDirectionIndex]);
		//Add a decoy direction to selectedLetters(index 4, LEAVE THIS ON UNITYENGINE.RANDOM.RANGE)
		List<int> unchosenDirections = new List<int>{0, 1, 2, 3, 4, 5};
		unchosenDirections.Remove(solutionDirectionIndex);
		selectedLetters.Add(DirectionLetters[unchosenDirections[UnityEngine.Random.Range(0, 5)]]);
		souvenirList = selectedLetters.Select(x => x.name).ToArray();
		string logMaze = "";
		foreach(Cell cell in SelectedMaze)
		{
			string[] logWalls = cell.Walls.Select(x => x.Exists ? "1" : "0").ToArray();
			logMaze += $"{logWalls[0]}{logWalls[2]}{logWalls[4]}{(cell.Letter == -1 || cell.Letter == 4 ? "" : cell.Letter < 3 ? selectedLetters[cell.Letter].name : "✲")}\n";
		}

		Debug.Log(logMaze);
		//Debug.Log($"selectedLetters has length {selectedLetters.Count}, which are {String.Join(", ", selectedLetters.Select(x => x.name).ToArray())}");
		foreach(Cell cell in SelectedMaze)
		{
			cell.LetterMats = selectedLetters;
			cell.updateDebugCell();
		}
		Log($"Using maze with markings {Markings}.");
		//LEAVE THIS ON UNITYENGINE.RANDOM.RANGE AS IT IS PICKING A RANDOM START POSITION & ORIENTATION
		currentCell = SelectedMaze[UnityEngine.Random.Range(0,4), UnityEngine.Random.Range(0,4), UnityEngine.Random.Range(0,4)];
		//Randomise rotation by first picking a direction to point in, and then a number of times to roll around local forward
		int random = UnityEngine.Random.Range(0, 6);
		switch(random)
		{
			case 0:
			case 1:
			case 2:
			case 3:
				//Turn left the selected number of times
				for(int i = 0; i < random; i++)
				{
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{5, 1, 4, 0});
				}
				break;
			case 4:
				//Face upwards
				localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{2, 4, 3, 5});
				break;
			case 5:
				//Face down
				localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{5, 3, 4, 2});
				break;
			
		}
		random = UnityEngine.Random.Range(0, 4);
		//Roll clockwise the selected number of times
		for(int i = 0; i < random; i++)
		{
			localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{2, 1, 3, 0});
		}
		updateDisplay();
		cursor.transform.localPosition = new Vector3Int(currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z);
		cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
		
	}
	//                                                      Right(0)       Left(1)        Up(2)          Down(3)        Forward(4)     Backward(5)
	Vector3Int[] localToGlobalDirections = new Vector3Int[]{Directions[0], Directions[1], Directions[2], Directions[3], Directions[4], Directions[5]};
	//This method returns the coordinate gained from adding the input Vector3Int to the input coordinate, relative to viewer's local axes
	Vector3Int relativeCoord(Vector3Int location, Vector3Int toAdd)
	{
		int[] toAddArray = new int[]{toAdd.x, toAdd.y, toAdd.z};
		int[] output = new int[3];
		int[] localDirection;
		int sign;
		int affectedIndex;
		Vector3Int globalDirection;
		int[] globalDirectionArray;
		for(int i = 0; i < 3; i++)
		{
			localDirection = new int[]{0, 0, 0};
			sign = Math.Sign(toAddArray[i]) != 0 ? Math.Sign(toAddArray[i]) : 1;
			localDirection[i] = sign;
			globalDirection =  localToGlobalDirections[Array.IndexOf(Directions, new Vector3Int(localDirection[0], localDirection[1], localDirection[2]))];
			globalDirectionArray = new int[]{globalDirection.x, globalDirection.y, globalDirection.z};
			affectedIndex = Array.IndexOf(Directions, globalDirection) < 2 ? 0 : (Array.IndexOf(Directions, globalDirection) < 4 ? 1 : 2);
			output[affectedIndex] = globalDirectionArray[affectedIndex] * Math.Abs(toAddArray[i]);
		}
		return location + new Vector3Int(output[0], output[1], output[2]);
	}

	//This method returns the input Vector3Int array with each given index cycled to the next given index by 1, wrapping around
	Vector3Int[] Cycle(Vector3Int[] input, int[] indices)
	{
		Vector3Int[] output = input.ToArray();
		output[indices[0]] = input[indices[indices.Length - 1]];
		for(int i = 1; i < indices.Length; i++)
		{
			output[indices[i]] = input[indices[i - 1]];
		}
		return output;
	}
	void ButtonPress(int index)
	{
		Vector3 holdposition =  MovementButtons[index].transform.localPosition;
		MovementButtons[index].transform.localPosition = new Vector3(holdposition.x, 0.06f, holdposition.z);
		MovementButtons[index].AddInteractionPunch();
		GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		if(!moduleSolved)
		{
			switch(index)
			{
				case 0:
					//Move forward
					if(!currentCell.Walls[Array.IndexOf(Directions, localToGlobalDirections[4])].Exists)
					{
						Vector3Int endLocation = currentCell.Coords + localToGlobalDirections[4];
						currentCell = SelectedMaze[endLocation.x, endLocation.y, endLocation.z];
					}
					//Hit the goal wall from the front
					else if((currentCell == solutionCell && localToGlobalDirections[4] == Directions[solutionDirectionIndex]) ||
					//or back
					(currentCell.Coords == solutionCell.Coords + Directions[solutionDirectionIndex] && localToGlobalDirections[4] == Directions[solutionDirectionIndex] * -1))
					{
						Log($"Correctly moved into the {new string[]{"East", "West", "Up", "Down", "North", "South"}[solutionDirectionIndex]} wall of {solutionCell.Coords}. Solved.");
						moduleSolved = true;
						LetterDisplay.SetActive(false);
						FarLetterDisplay.SetActive(false);
						foreach(GameObject wall in Sprites)
						{
							if(wall != null)
							{
								wall.SetActive(false);
							}
						}
						GetComponent<KMBombModule>().HandlePass();
					}
					//Run into an incorrect wall
					else
					{
						Log($"Incorrectly moved into the {new string[]{"East", "West", "Up", "Down", "North", "South"}[Array.IndexOf(Directions, localToGlobalDirections[4])]} wall of {currentCell.Coords}. Striking.");
						GetComponent<KMBombModule>().HandleStrike();
					}
					break;
				case 1:
					//Look up
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{2, 4, 3, 5});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
				case 2:
					//Look down
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{5, 3, 4, 2});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
				case 3:
					//Look left
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{5, 1, 4, 0});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
				case 4:
					//Look right
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{0, 4, 1, 5});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
				case 5:
					//Roll anticlockwise
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{0, 3, 1, 2});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
				case 6:
					//Roll clockwise
					localToGlobalDirections = Cycle(localToGlobalDirections, new int[]{2, 1, 3, 0});
					cursor.transform.localRotation = Quaternion.LookRotation(localToGlobalDirections[1], localToGlobalDirections[2]);
					break;
			}
			if(!moduleSolved)
			{
				updateDisplay();
			}
		}
	}
	void updateDisplay()
	{
		Vector3Int location;
		int[] locationArray;
		bool OOB;
		for(int i = 0; i < 2; i++)
		{
			for(int j = -1; j < 2; j++)
			{
				for(int k = -1; k < 2; k++)
				{
					location = relativeCoord(currentCell.Coords, new Vector3Int(k, j, i));
					locationArray = new int[]{location.x, location.y, location.z};
					OOB = false;
					foreach(int coord in locationArray)
					{
						if(coord > 3 || coord < 0)
						{
							OOB = true;
						}
					}
					if(displayArray[k + 1, j + 1, i] != null)
					{
						foreach(GameObject wall in displayArray[k + 1, j + 1, i].transform.Cast<Transform>().Select(x => x.gameObject).ToArray())
						{
							if(OOB || SelectedMaze[locationArray[0], locationArray[1], locationArray[2]].Walls[Array.IndexOf(Directions, localToGlobalDirections[int.Parse(wall.name)])].Exists)
							{
								wall.SetActive(true);
							}
							else
							{
								wall.SetActive(false);
							}
						}
					}
				}
			}
		}
		if(currentCell.Letter != -1)
		{
			LetterDisplay.SetActive(true);
			LetterDisplay.GetComponent<Renderer>().material = currentCell.LetterMats[currentCell.Letter];
		}
		else
		{
			LetterDisplay.SetActive(false);
		}
		int[] cellInFront = new int[]{currentCell.Coords.x + localToGlobalDirections[4].x, currentCell.Coords.y + localToGlobalDirections[4].y, currentCell.Coords.z + localToGlobalDirections[4].z};
		if(!(cellInFront.Contains(-1) || cellInFront.Contains(4)))
		{
			if(SelectedMaze[cellInFront[0], cellInFront[1], cellInFront[2]].Letter != -1)
			{
				FarLetterDisplay.SetActive(true);
			}
			else
			{
				FarLetterDisplay.SetActive(false);
			}
		}
		cursor.transform.localPosition = new Vector3Int(currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z);
	}
	void LiftButton(int index)
	{
		Vector3 holdposition =  MovementButtons[index].transform.localPosition;
		MovementButtons[index].transform.localPosition = new Vector3(holdposition.x, 0.08f, holdposition.z);
	}
	
	public List<Cell> CellList = new List<Cell>();
	Cell currentCell;
	CoordAndDirection? result = new CoordAndDirection();
	int cellNumber = 0;
	List<Cell>[] lockedWallsCount;
	void WFC(Cell[,,] Maze){
		//lockedWallsCount contains all cells that aren't fully collapsed, each cell at the index matching the number of locked walls it has
		lockedWallsCount = new List<Cell>[6];
		bool lockedWallsCountEmpty = false;
		//The highest indexed list in lockedWallsCount whose count isn't 0
		int highestLocked;
		for(int i = 0; i < 6; i++)
		{
			lockedWallsCount[i] = new List<Cell>();
		}
		foreach(Cell cell in Maze)
		{
			cell.visited = true;
			lockedWallsCount[0].Add(cell);
		}
		currentCell = Maze[rnd.Next(0,4), rnd.Next(0,4), rnd.Next(0,4)];

		//Generate the initial maze, which may have inaccessible areas
		while(!lockedWallsCountEmpty)
		{
			currentCell.Walls = validWallConfiguration(currentCell);
			lockedWallsCountRemove(currentCell);
			currentCell.updateDebugCell();
			foreach(Vector3Int direction in Directions)
			{
				bool OOB = false;
				Vector3Int targetCellCoords = currentCell.Coords + direction;
				foreach(int i in new int[]{targetCellCoords.x, targetCellCoords.y, targetCellCoords.z})
				{
					if(i < 0 || i > 3)
					{
						OOB = true;
					}
				}
				if(!OOB)
				{
					Cell targetCell = Maze[targetCellCoords.x, targetCellCoords.y, targetCellCoords.z];
					targetCell.Walls[Array.IndexOf(Directions, direction * -1)] = currentCell.Walls[Array.IndexOf(Directions, direction)];
					targetCell.Walls[Array.IndexOf(Directions, direction * -1)].stateLocked = true;
					targetCell.updateDebugCell();
					lockedWallsCountRemove(targetCell);
					if(targetCell.lockedWalls < 6)
					{
						lockedWallsCount[targetCell.lockedWalls].Add(targetCell);
					}
				}
			}
			highestLocked = 0;
			for(int i = 0; i < 6; i++)
			{
				if(lockedWallsCount[i].Count > 0)
				{
					highestLocked = i;
				}
			}
			lockedWallsCountEmpty = true;
			for(int i = 0; i < 6; i++)
			{
				if(lockedWallsCount[i].Count > 0)
				{
					lockedWallsCountEmpty = false;
					currentCell = lockedWallsCount[highestLocked][rnd.Next(0, lockedWallsCount[highestLocked].Count)];
				}
			}
		}
		//Determine inaccessible cells(if any), make a random inaccessible cell accessible by knocking down a wall, and repeat until all 64 cells are accessible
		while(lookForInaccessibleAreas(Maze))
		{
			int randomIndex = rnd.Next(0, UnvisitedList.Count);
			result = RandomConditionalNeighbourAndDirection(UnvisitedList[randomIndex], true, false, Maze);
			while(result == null)
			{
				UnvisitedList.RemoveAt(randomIndex);
				randomIndex = rnd.Next(0, UnvisitedList.Count);
				result = RandomConditionalNeighbourAndDirection(UnvisitedList[randomIndex], true, false, Maze);
			}
			UnvisitedList[randomIndex].Walls[result.Value.Direction].Exists = false;
			UnvisitedList[randomIndex].updateDebugCell();
			Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].Walls[Array.IndexOf(Directions, Directions[result.Value.Direction] * -1)].Exists = false;
			Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].updateDebugCell();
			//Debug.Log($"Connected {UnvisitedList[randomIndex].Coords} to {Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].Coords}.");
		}
	}
	List<Cell> UnvisitedList;

	//Use recursive backtracking to look for inaccessible areas
	bool lookForInaccessibleAreas(Cell[,,] Maze)
	{
		//Only inaccessible cells will remain in this list by the end of the method
		UnvisitedList = new List<Cell>();
		foreach(Cell cell in Maze)
		{
			UnvisitedList.Add(cell);
		}
		
		CellList = new List<Cell>();
		foreach(Cell cell in Maze)
		{
			cell.visited = false;
		}
		currentCell = Maze[rnd.Next(0,4), rnd.Next(0,4), rnd.Next(0,4)];
		currentCell.visited = true;
		UnvisitedList.Remove(currentCell);
		if(DebugMode)
		{
			Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].GetComponentInChildren<TextMesh>().text = "0";
		}
		//Add the first cell to CellList manually because the method only adds it if it doesn't use up its last neighbour
		//and the while loop requires the first cell to be in CellList already in all cases
		result = randomConnectedUnvisitedNeighbourAndDirection(currentCell, false, Maze, false);
		CellList.Add(currentCell);
		int cellNumber = 0;
		//Debug.Log(CellList.Count);
		//Debug.Log(result);
		while(CellList.Count > 0)
		{
			while(result != null)
			{
				currentCell = Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z];
				currentCell.visited = true;
				UnvisitedList.Remove(currentCell);
				if(DebugMode)
				{
					Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].GetComponentInChildren<TextMesh>().text = $"{cellNumber + 1}";
				}
				result = randomConnectedUnvisitedNeighbourAndDirection(currentCell, true, Maze, false);
				cellNumber++;
			}
			currentCell.visited = true;
			UnvisitedList.Remove(currentCell);
			//Debug.Log($"Dead end after {cellNumber}. Backtracking.");
			while(result == null && CellList.Count > 0)
			{
				CellList.Remove(currentCell);
				if(CellList.Count > 0)
				{
					currentCell = CellList[CellList.Count - 1];
				}
				currentCell.visited = true;
				UnvisitedList.Remove(currentCell);
				result = randomConnectedUnvisitedNeighbourAndDirection(currentCell, false, Maze, false);
				if(result != null)
				{
					//Debug.Log(result.Value.Coords);
				}
			}
			//Debug.Log(String.Join(",", CellList.Select(x => x.Coords.ToString()).ToArray()));
		}
		//Debug.Log($"{cellNumber} ({currentCell.Coords})");
		//Debug.Log(cellNumber != 63 ? $"Inaccessible areas detected: {String.Join(",", UnvisitedList.Select(x => x.Coords.ToString()).ToArray())}" : "No inaccessible areas detected");
		return cellNumber != 63;
	}
	void lockedWallsCountRemove(Cell cell)
	{
		for(int i = 0; i < 6; i++)
		{
			lockedWallsCount[i].Remove(cell);
		}
	}
	void GrowingTree(Cell[,,] Maze) {
		CellList = new List<Cell>();
		currentCell = Maze[rnd.Next(0,4), rnd.Next(0,4), rnd.Next(0,4)];
		currentCell.visited = true;
		CellList.Add(currentCell);
		while(CellList.Count > 0)
		{
			if(DebugMode)
			{
				if(Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].transform.Find("Text").GetComponent<TextMesh>().text == "x")
				{
					Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].transform.Find("Text").GetComponent<TextMesh>().text = cellNumber.ToString();
				}
			}
			int i = rnd.Next(0, CellList.Count);
			result = RandomConditionalNeighbourAndDirection(CellList[i], false, false, Maze);
			if(result != null)
			{
				currentCell = CellList[i];
				currentCell.visited = true;
				CellList.Add(currentCell);
				currentCell.RemoveWall(result.Value.Direction);
				cellNumber++;
				Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].visited = true;
				if(DebugMode)
				{
					Cell.VisualiserCells[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].transform.Find("Text").GetComponent<TextMesh>().text = cellNumber.ToString();
				}
				CellList.Add(Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z]);
				Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z].RemoveWall(result.Value.Direction % 2 == 1 ? result.Value.Direction - 1 : result.Value.Direction + 1);
			}
			else
			{
				CellList.RemoveAt(i);
			}
		}
	}
	void RecursiveBacktracking(Cell[,,] Maze) {
		CellList = new List<Cell>();
		int TimesBacktracked = 0;
		currentCell = Maze[rnd.Next(0,4), rnd.Next(0,4), rnd.Next(0,4)];
		//Debug.Log($"Started at {currentCell.Coords.x}, {currentCell.Coords.y}, {currentCell.Coords.z}.");
		currentCell.visited = true;
		cellNumber = 0;
		result = null;
		while(cellNumber < 63)
		{
			while(result != null)
			{
				if(DebugMode)
				{
					if(Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].transform.Find("Text").GetComponent<TextMesh>().text == "x")
					{
						Cell.VisualiserCells[currentCell.Coords.x, currentCell.Coords.y, currentCell.Coords.z].transform.Find("Text").GetComponent<TextMesh>().text = cellNumber.ToString();
					}
				}
				result = RandomConditionalNeighbourAndDirection(currentCell, false, true, Maze);
				if(result != null)
				{
					currentCell.RemoveWall(result.Value.Direction);
					//currentCell.SetColour(VisualiserMats[TimesBacktracked < 7 ? TimesBacktracked : 6]);
					currentCell = Maze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z];
					CellList.Add(currentCell);
					currentCell.visited = true;
					//Remove the touching wall on the next cell
					currentCell.RemoveWall(Array.IndexOf(Directions, Directions[result.Value.Direction] * -1));
					cellNumber++;
				}
				else
				{
					//currentCell.SetColour(VisualiserMats[TimesBacktracked < 7 ? TimesBacktracked : 6]);
					//Debug.Log("REACHED A DEAD END. BACKTRACKING..");
				}
			}
			while(result == null && cellNumber < 63)
			{
				if(CellList.Count > 0)
				{
					currentCell = CellList[CellList.Count - 1];
				}
				
				result = RandomConditionalNeighbourAndDirection(currentCell, false, false, Maze);
				if(result == null)
				{
					CellList.RemoveAt(CellList.Count - 1);
				}
			}
			TimesBacktracked++;
		}
	}

	Wall[] validWallConfiguration(Cell inputCell)
	{
		List<int> unlockedIndices = new List<int>();
		Wall[] wallsToModify = inputCell.Walls.ToArray();
		int wallsLockedToExist = 0;
		//prevent the method from knocking down a wall that leads out of bounds
		foreach(Vector3Int direction in Directions)
		{
			Vector3Int targetDirection = inputCell.Coords + direction;
			bool OOB = false;
			foreach(int i in new int[]{targetDirection.x, targetDirection.y, targetDirection.z})
			{
				if(i < 0 || i > 3)
				{
					OOB = true;
				}
			}
			if(OOB)
			{
				inputCell.Walls[Array.IndexOf(Directions, direction)].Exists = true;
				inputCell.Walls[Array.IndexOf(Directions, direction)].stateLocked = true;
			}
		}
		for(int i = 0; i < 6; i++)
		{
			if(inputCell.Walls[i].stateLocked && inputCell.Walls[i].Exists)
			{
				wallsLockedToExist++;
			}
			else if(!inputCell.Walls[i].stateLocked)
			{
				unlockedIndices.Add(i);
			}
			//All walls should be locked in the output configuration
			wallsToModify[i].stateLocked = true;
		}
		//Return the input configuration if all walls are already locked
		if(unlockedIndices.Count == 0)
		{
			return wallsToModify;
		}
		//If wallsLockedToExist is at least 1 and less than the total number of locked walls, then there must be at least one wall locked to each state
		if(wallsLockedToExist > 0 && wallsLockedToExist < 6 - unlockedIndices.Count)
		{
			//So it's now safe to fill the rest of the walls in randomly without getting a cell that's completely closed or completely open
			foreach(int i in unlockedIndices)
			{
				wallsToModify[i].Exists = rnd.Next(0, 2) == 0 ? true : false;
			}
		}
		//There is at least 1 unlocked wall, and all the locked walls(if there are any) are locked to the same state
		//Pick a random
		else
		{
			int randomIndex;
			if(wallsLockedToExist == 0)
			{
				randomIndex = rnd.Next(0, unlockedIndices.Count);
				wallsToModify[unlockedIndices[randomIndex]].Exists = true;
				unlockedIndices.RemoveAt(randomIndex);
			}
			//If wallsLockedToExist == total number of locked walls, there can't be any walls locked to not exist
			if(wallsLockedToExist == 6 - unlockedIndices.Count)
			{
				randomIndex = rnd.Next(0, unlockedIndices.Count);
				wallsToModify[unlockedIndices[randomIndex]].Exists = false;
				unlockedIndices.RemoveAt(randomIndex);
			}
			//Now that we've guaranteed that there's at least one wall locked to each state
			foreach(int i in unlockedIndices)
			{
				//It's safe to fill the rest of the walls in randomly without getting a cell that's completely closed or completely open
				wallsToModify[i].Exists = rnd.Next(0, 2) == 0 ? true : false;
			}
		}
		return wallsToModify;
	}

	//Returns a struct containing unvisited neighbour of the input cell, or null if all neighbours are visited
	CoordAndDirection? RandomConditionalNeighbourAndDirection(Cell inputCell, bool visited_, bool logAndAddToList, Cell[,,] Maze)
	{
		Vector3Int Coords = inputCell.Coords;
		List<CoordAndDirection> UnvisitedNeighbours = new List<CoordAndDirection>();

		foreach(Vector3Int direction in Directions)
		{
			Vector3Int target = Coords + direction;
			if(target.x >= 0 && target.x < 4 &&
			target.y >= 0 && target.y < 4 &&
			target.z >= 0 && target.z < 4)
			{
				if(Maze[target.x, target.y, target.z].visited == visited_)
				{
					UnvisitedNeighbours.Add(new CoordAndDirection {Coords = new Vector3Int(target.x, target.y, target.z), Direction = Array.IndexOf(Directions, direction)});
				}
			}
		}
		if(UnvisitedNeighbours.Count > 0)
		{
			CoordAndDirection selectedNeighbour = UnvisitedNeighbours[rnd.Next(0, UnvisitedNeighbours.Count)];
			if(logAndAddToList)
			{
				//No point in adding the input cell to the recursion tracker if we're about to use up its last neighbour
				if(UnvisitedNeighbours.Count > 1)
				{
					CellList.Add(inputCell);
				}
				//Debug.Log($"{cellNumber + 1}. Returned {selectedNeighbour.Coords.x}, {selectedNeighbour.Coords.y}, {selectedNeighbour.Coords.z}, which is in direction {Directions[selectedNeighbour.Direction].x}, {Directions[selectedNeighbour.Direction].y}, {Directions[selectedNeighbour.Direction].z}.");
			}
			return selectedNeighbour;
		}
		return null;
	}
	private readonly string TwitchHelpMessage = @"3Der Maze: !{0} S [move forward] | !{0} A [turn left] | !{0} D [turn right] | !{0} W [look up] | !{0} X [look down] | !{0} Q [roll anticlockwise] | !{0} E [roll clockwise] Commands may be chained in a single message, with or without spaces.";
	IEnumerator ProcessTwitchCommand(string command)
	{
		command = command.ToLowerInvariant();

		char[] orderArray = new char[]{'s', 'w', 'x', 'a', 'd', 'q', 'e'};
		if(!Regex.IsMatch(command, @"([^sadwxqe ])"))
		{
			yield return null;
			command = command.Replace(" ", "");
			foreach(char letter in command)
			{
				yield return MovementButtons[Array.IndexOf(orderArray, letter)];
				yield return new WaitForSeconds(0.1f);
				yield return MovementButtons[Array.IndexOf(orderArray, letter)];
				yield return new WaitForSeconds(0.4f);
			}
		}
	}
	IEnumerator TwitchHandleForcedSolve()
	{
		WaitForSeconds holdDownButtonFor = new WaitForSeconds(0.1f);
		WaitForSeconds delayBetweenButtons = new WaitForSeconds(0.2f);
		Cell stepTwoCell = currentCell;
		//Use recursive backtracking to find a path to a solution wall. Conveniently, CellList will hold in order the cells that make up this path at the end of this section.
		foreach(Cell cell in SelectedMaze)
		{
			cell.visited = false;
		}
		CellList = new List<Cell>();
		
		while(currentCell != solutionCell && currentCell.Coords != solutionCell.Coords + Directions[solutionDirectionIndex])
		{
			currentCell.visited = true;
			while(randomConnectedUnvisitedNeighbourAndDirection(currentCell, false, SelectedMaze, false) != null && currentCell != solutionCell && currentCell.Coords != solutionCell.Coords + Directions[solutionDirectionIndex])
			{
				result = randomConnectedUnvisitedNeighbourAndDirection(currentCell, true, SelectedMaze, true);
				currentCell = SelectedMaze[result.Value.Coords.x, result.Value.Coords.y, result.Value.Coords.z];
				currentCell.visited = true;
				//Debug.Log(currentCell.Coords);
			}
			int loopcounter = 0;
			while(randomConnectedUnvisitedNeighbourAndDirection(currentCell, false, SelectedMaze, false) == null && currentCell != solutionCell && currentCell.Coords != solutionCell.Coords + Directions[solutionDirectionIndex])
			{
				//Debug.Log("Reached a dead end. Backtracking..");
				//Debug.Log(CellList.Contains(currentCell));
				CellList.Remove(currentCell);
				currentCell = CellList[CellList.Count - 1];
				loopcounter++;
			}
			//Debug.Log(loopcounter);
		}
		//Debug.Log($"{currentCell != solutionCell} {currentCell.Coords != solutionCell.Coords + Directions[solutionDirectionIndex]}");
		CellList.Add(currentCell);
		CellList.Add(new Cell(){Coords = (currentCell == solutionCell ? currentCell.Coords + Directions[solutionDirectionIndex] : currentCell.Coords - Directions[solutionDirectionIndex])});
		//Debug.Log(String.Join(", ", CellList.Select(x => x.Coords.ToString()).ToArray()));

		currentCell = stepTwoCell;
		foreach(Cell cell in CellList)
		{
			Vector3Int relativeDirection = cell.Coords - currentCell.Coords;
			if(relativeDirection != new Vector3Int(0,0,0))
			{
				switch(Array.IndexOf(localToGlobalDirections, relativeDirection))
				{
					case 0:
						MovementButtons[4].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[4].OnInteractEnded();
						yield return delayBetweenButtons;
						break;
					case 1:
						MovementButtons[3].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[3].OnInteractEnded();
						yield return delayBetweenButtons;
						break;
					case 2:
						MovementButtons[1].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[1].OnInteractEnded();
						yield return delayBetweenButtons;
						break;
					case 3:
						MovementButtons[2].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[2].OnInteractEnded();
						yield return delayBetweenButtons;
						break;
					case 4:
						break;
					case 5:
						MovementButtons[4].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[4].OnInteractEnded();
						yield return delayBetweenButtons;
						MovementButtons[4].OnInteract();
						yield return holdDownButtonFor;
						MovementButtons[4].OnInteractEnded();
						yield return delayBetweenButtons;
						break;
				}
				//Debug.Log(relativeDirection);
				MovementButtons[0].OnInteract();
				yield return holdDownButtonFor;
				MovementButtons[0].OnInteractEnded();
				yield return delayBetweenButtons;
			}
		}
	}
	CoordAndDirection? randomConnectedUnvisitedNeighbourAndDirection(Cell inputCell, bool logAndAddToList, Cell[,,] Maze, bool addEvenIfLast)
	{
		List<CoordAndDirection> validOptions = new List<CoordAndDirection>();
		foreach(Vector3Int direction in Directions)
		{
			Vector3Int targetCoord = inputCell.Coords + direction;
			bool OOB = false;
			foreach(int i in new int[]{targetCoord.x, targetCoord.y, targetCoord.z})
			{
				if(i < 0 || i > 3)
				{
					OOB = true;
				}
			}
			//If the cell at targetCoord is in-bounds AND has a passage to the inputCell AND unvisited
			if(!OOB)
			{
				if(!inputCell.Walls[Array.IndexOf(Directions, direction)].Exists && !Maze[targetCoord.x, targetCoord.y, targetCoord.z].visited)
				{
					validOptions.Add(new CoordAndDirection{Coords = targetCoord, Direction = Array.IndexOf(Directions, direction)});
				}
			}
		}
		if(validOptions.Count > 0)
		{
			if(logAndAddToList)
			{
				if(validOptions.Count > 1 || addEvenIfLast)
				{
					CellList.Add(inputCell);
				}
			}
			return(validOptions[rnd.Next(0, validOptions.Count)]);
		}
		return null;
	}
	void Log(string message)
	{
		Debug.Log($"[3Der Maze #{moduleId}] {message}");
	}
}
