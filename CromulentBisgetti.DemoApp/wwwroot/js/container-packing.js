var scene;
var camera;
var renderer;
var controls;
var viewModel;
var itemMaterial;

async function PackContainers(request) {
	return $.ajax({
		url: '/api/containerpacking',
		type: 'POST',
		data: request,
		contentType: 'application/json; charset=utf-8'
	});
};

function InitializeDrawing() {
	var container = $('#drawing-container');

	scene = new THREE.Scene();
	camera = new THREE.PerspectiveCamera( 50, window.innerWidth/window.innerHeight, 0.1, 1000 );
	camera.lookAt(scene.position);

	//var axisHelper = new THREE.AxisHelper( 5 );
	//scene.add( axisHelper );

	// LIGHT
	var light = new THREE.PointLight(0xffffff);
	light.position.set(0,150,100);
	scene.add(light);

	// Get the item stuff ready.
	itemMaterial = new THREE.MeshNormalMaterial( { transparent: true, opacity: 0.6 } );

	renderer = new THREE.WebGLRenderer( { antialias: true } ); // WebGLRenderer CanvasRenderer
	renderer.setClearColor( 0xf0f0f0 );
	renderer.setPixelRatio( window.devicePixelRatio );
	renderer.setSize( window.innerWidth / 2, window.innerHeight / 2);
	container.append( renderer.domElement );

	controls = new THREE.OrbitControls( camera, renderer.domElement );
	window.addEventListener( 'resize', onWindowResize, false );

	animate();
};

function onWindowResize() {
	camera.aspect = window.innerWidth / window.innerHeight;
	camera.updateProjectionMatrix();
	renderer.setSize( window.innerWidth / 2, window.innerHeight / 2 );
}
//
function animate() {
	requestAnimationFrame( animate );
	controls.update();
	render();
}
function render() {
	renderer.render( scene, camera );
}

var ViewModel = function () {
	var self = this;

	self.ItemCounter = 0;
	self.ContainerCounter = 0;

	self.ItemsToRender = ko.observableArray([]);
	self.LastItemRenderedIndex = ko.observable(-1);

	self.ContainerOriginOffset = {
		x: 0,
		y: 0,
		z: 0
	};

	self.AlgorithmsToUse = ko.observableArray([]);
	self.ItemsToPack = ko.observableArray([]);
	self.Containers = ko.observableArray([]);

	self.NewItemToPack = ko.mapping.fromJS(new ItemToPack());
	self.NewContainer = ko.mapping.fromJS(new Container());

	self.GenerateContainers = function () {
		self.Containers([]);
		self.Containers.push(ko.mapping.fromJS({ ID: 1000, Name: 'Tır1', Length: 250, Width: 100, Height: 100, AlgorithmPackingResults: [] }));
	};

	self.GenerateItemsToPack = function () {
		self.ItemsToPack([]);
		//self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1000, SupplierId: 206, Name: 'Palet1', Length: 25, Width: 25, Height: 25, Quantity: 1 }));

		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1000, SupplierId: 206, Name: 'Palet1',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1001, SupplierId: 206, Name: 'Palet2',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1002, SupplierId: 206, Name: 'Palet3',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1003, SupplierId: 206, Name: 'Palet4',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1004, SupplierId: 206, Name: 'Palet5',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1005, SupplierId: 206, Name: 'Palet6',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1006, SupplierId: 206, Name: 'Palet7',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1007, SupplierId: 206, Name: 'Palet8',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1008, SupplierId: 206, Name: 'Palet9',  Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1009, SupplierId: 206, Name: 'Palet10', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1010, SupplierId: 206, Name: 'Palet11', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1011, SupplierId: 206, Name: 'Palet12', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1012, SupplierId: 206, Name: 'Palet13', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1013, SupplierId: 206, Name: 'Palet14', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1014, SupplierId: 206, Name: 'Palet15', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1015, SupplierId: 206, Name: 'Palet16', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1016, SupplierId: 206, Name: 'Palet17', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1017, SupplierId: 206, Name: 'Palet18', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1018, SupplierId: 206, Name: 'Palet19', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1019, SupplierId: 206, Name: 'Palet20', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1020, SupplierId: 206, Name: 'Palet21', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1021, SupplierId: 206, Name: 'Palet22', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1022, SupplierId: 206, Name: 'Palet23', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1023, SupplierId: 206, Name: 'Palet24', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1024, SupplierId: 206, Name: 'Palet25', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1025, SupplierId: 206, Name: 'Palet26', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1026, SupplierId: 206, Name: 'Palet27', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1027, SupplierId: 206, Name: 'Palet28', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1028, SupplierId: 206, Name: 'Palet29', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1029, SupplierId: 206, Name: 'Palet30', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1030, SupplierId: 206, Name: 'Palet31', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1031, SupplierId: 206, Name: 'Palet32', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1032, SupplierId: 205, Name: 'Palet33', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1033, SupplierId: 205, Name: 'Palet34', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1034, SupplierId: 205, Name: 'Palet35', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1035, SupplierId: 205, Name: 'Palet36', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1036, SupplierId: 205, Name: 'Palet37', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1037, SupplierId: 205, Name: 'Palet38', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1038, SupplierId: 205, Name: 'Palet39', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1039, SupplierId: 205, Name: 'Palet40', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1040, SupplierId: 205, Name: 'Palet41', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1041, SupplierId: 205, Name: 'Palet42', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1042, SupplierId: 205, Name: 'Palet43', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1043, SupplierId: 205, Name: 'Palet44', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1044, SupplierId: 205, Name: 'Palet45', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1045, SupplierId: 205, Name: 'Palet46', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1046, SupplierId: 205, Name: 'Palet47', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1047, SupplierId: 205, Name: 'Palet48', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1048, SupplierId: 205, Name: 'Palet49', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1049, SupplierId: 205, Name: 'Palet50', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1050, SupplierId: 205, Name: 'Palet51', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1051, SupplierId: 205, Name: 'Palet52', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1052, SupplierId: 205, Name: 'Palet53', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1053, SupplierId: 205, Name: 'Palet54', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1054, SupplierId: 205, Name: 'Palet55', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1055, SupplierId: 195, Name: 'Palet56', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1056, SupplierId: 195, Name: 'Palet57', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1057, SupplierId: 195, Name: 'Palet58', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1058, SupplierId: 195, Name: 'Palet59', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1059, SupplierId: 195, Name: 'Palet60', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1060, SupplierId: 195, Name: 'Palet61', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1061, SupplierId: 195, Name: 'Palet62', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1062, SupplierId: 130, Name: 'Palet63', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1063, SupplierId: 190, Name: 'Palet64', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1064, SupplierId: 201, Name: 'Palet65', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
		self.ItemsToPack.push(ko.mapping.fromJS({ ID: 1065, SupplierId: 211, Name: 'Palet66', Length: 50, Width: 50, Height: 100, Quantity: 1 }));
	};

	self.AddAlgorithmToUse = function () {
		var algorithmID = $('#algorithm-select option:selected').val();
		var algorithmName = $('#algorithm-select option:selected').text();
		self.AlgorithmsToUse.push({ AlgorithmID: algorithmID, AlgorithmName: algorithmName });
	};

	self.RemoveAlgorithmToUse = function (item) {
		self.AlgorithmsToUse.remove(item);
	};

	self.AddNewItemToPack = function () {
		self.NewItemToPack.ID(self.ItemCounter++);
		self.ItemsToPack.push(ko.mapping.fromJS(ko.mapping.toJS(self.NewItemToPack)));
		self.NewItemToPack.Name('');
		self.NewItemToPack.Length('');
		self.NewItemToPack.Width('');
		self.NewItemToPack.Height('');
		self.NewItemToPack.Quantity('');
	};

	self.RemoveItemToPack = function (item) {
		self.ItemsToPack.remove(item);
	};

	self.AddNewContainer = function () {
		self.NewContainer.ID(self.ContainerCounter++);
		self.Containers.push(ko.mapping.fromJS(ko.mapping.toJS(self.NewContainer)));
		self.NewContainer.Name('');
		self.NewContainer.Length('');
		self.NewContainer.Width('');
		self.NewContainer.Height('');
	};

	self.RemoveContainer = function (item) {
		self.Containers.remove(item);
	};

	self.PackContainers = function () {
		var algorithmsToUse = [];

		self.AlgorithmsToUse().forEach(algorithm => {
			algorithmsToUse.push(algorithm.AlgorithmID);
		});
		
		var itemsToPack = [];

		self.ItemsToPack().forEach(item => {
			var itemToPack = {
				ID: item.ID(),
				Dim1: item.Length(),
				Dim2: item.Width(),
				Dim3: item.Height(),
				Quantity: item.Quantity()
			};
			
			itemsToPack.push(itemToPack);
		});
		
		var containers = [];

		// Send a packing request for each container in the list.
		self.Containers().forEach(container => {
			var containerToUse = {
				ID: container.ID(),
				Length: container.Length(),
				Width: container.Width(),
				Height: container.Height()
			};

			containers.push(containerToUse);
		});
		
		// Build container packing request.
		var request = {
			Containers: containers,
			ItemsToPack: itemsToPack,
			AlgorithmTypeIDs: algorithmsToUse
		};
		
		PackContainers(JSON.stringify(request))
			.then(response => {
				// Tie this response back to the correct containers.
				response.forEach(containerPackingResult => {
					self.Containers().forEach(container => {
						if (container.ID() == containerPackingResult.ContainerID) {
							container.AlgorithmPackingResults(containerPackingResult.AlgorithmPackingResults);
						}
					});
				});
			});
	};
	
	self.ShowPackingView = function (algorithmPackingResult) {
		var container = this;
		var selectedObject = scene.getObjectByName('container');
		scene.remove( selectedObject );
		
		for (var i = 0; i < 1000; i++) {
			var selectedObject = scene.getObjectByName('cube' + i);
			scene.remove(selectedObject);
		}
		
		camera.position.set(container.Length(), container.Length(), container.Length());

		self.ItemsToRender(algorithmPackingResult.PackedItems);
		self.LastItemRenderedIndex(-1);

		self.ContainerOriginOffset.x = -1 * container.Length() / 2;
		self.ContainerOriginOffset.y = -1 * container.Height() / 2;
		self.ContainerOriginOffset.z = -1 * container.Width() / 2;

		var geometry = new THREE.BoxGeometry(container.Length(), container.Height(), container.Width());
		var geo = new THREE.EdgesGeometry( geometry ); // or WireframeGeometry( geometry )
		var mat = new THREE.LineBasicMaterial( { color: 0x000000, linewidth: 2 } );
		var wireframe = new THREE.LineSegments( geo, mat );
		wireframe.position.set(0, 0, 0);
		wireframe.name = 'container';
		scene.add( wireframe );
	};

	self.AreItemsPacked = function () {
		if (self.LastItemRenderedIndex() > -1) {
			return true;
		}

		return false;
	};

	self.AreAllItemsPacked = function () {
		if (self.ItemsToRender().length === self.LastItemRenderedIndex() + 1) {
			return true;
		}

		return false;
	};

	self.PackItemInRender = function () {
		var itemIndex = self.LastItemRenderedIndex() + 1;

		var itemOriginOffset = {
			x: self.ItemsToRender()[itemIndex].PackDimX / 2,
			y: self.ItemsToRender()[itemIndex].PackDimY / 2,
			z: self.ItemsToRender()[itemIndex].PackDimZ / 2
		};

		var itemGeometry = new THREE.BoxGeometry(self.ItemsToRender()[itemIndex].PackDimX, self.ItemsToRender()[itemIndex].PackDimY, self.ItemsToRender()[itemIndex].PackDimZ);
		var cube = new THREE.Mesh(itemGeometry, itemMaterial);
		cube.position.set(self.ContainerOriginOffset.x + itemOriginOffset.x + self.ItemsToRender()[itemIndex].CoordX, self.ContainerOriginOffset.y + itemOriginOffset.y + self.ItemsToRender()[itemIndex].CoordY, self.ContainerOriginOffset.z + itemOriginOffset.z + self.ItemsToRender()[itemIndex].CoordZ);
		cube.name = 'cube' + itemIndex;
		scene.add( cube );

		self.LastItemRenderedIndex(itemIndex);
	};

	self.UnpackItemInRender = function () {
		var selectedObject = scene.getObjectByName('cube' + self.LastItemRenderedIndex());
		scene.remove( selectedObject );
		self.LastItemRenderedIndex(self.LastItemRenderedIndex() - 1);
	};
};

var ItemToPack = function () {
	this.ID = '';
	this.Name = '';
	this.Length = '';
	this.Width = '';
	this.Height = '',
	this.Quantity = '';
}

var Container = function () {
	this.ID = '';
	this.Name = '';
	this.Length = '';
	this.Width = '';
	this.Height = '';
	this.AlgorithmPackingResults = [];
}

$(document).ready(() => {
	$('[data-toggle="tooltip"]').tooltip(); 
	InitializeDrawing();

	viewModel = new ViewModel();
	ko.applyBindings(viewModel);
});