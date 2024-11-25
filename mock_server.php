<?php
// Define routes and handle each endpoint
$uri = $_SERVER['REQUEST_URI'];
$method = $_SERVER['REQUEST_METHOD'];

// Check if the request method is POST
if ($method !== 'POST') {
    http_response_code(405);
    echo json_encode(['error' => 'Only POST requests are allowed']);
    exit();
}

// Read JSON input data
$inputData = json_decode(file_get_contents('php://input'), true);

// Define mock responses based on input for each endpoint
function getMockResponse($endpoint, $data) {
    switch ($endpoint) {
        case '/mock_server.php/api/checkRequestNationalNo':
            // Check for specific input NationalNo
            if (isset($data['NationalNo']) && $data['NationalNo'] == "1234567890") {
                return true;  
            }
            return ['error' => 'Invalid input data'];


        case '/mock_server.php/api/checkRequestInfo':
            // Check for specific input structure and values if needed
            if (isset($data['NationalNo'], $data['RecordNo']) && $data['NationalNo'] == "1234567890" && $data['RecordNo'] == "123456") {
                return [
                    'OTPNumber' => '1234'
                ];
            }
            return ['error' => 'Invalid input data'];

        case '/mock_server.php/api/Evaluation':
            // Check for specific input structure and values if needed
            if (isset($data['NationalNo'], $data['RecordNo'], 
            $data['MobileNo'], $data['Status'])) {
                if ($data['Status'] == "True") {
                    return true;
                }
                return false; // or return false;
            }
            return ['error' => 'Invalid input data'];

        case '/mock_server.php/api/GetRequestInfo':
            if (isset($data['NationalNo'], $data['RecordNo']) && $data['NationalNo'] == "1234567890" && $data['RecordNo'] == "123456") {
                 return [
                'StatusId' => rand(1, 4),
                'SoundMapId' => rand(1, 60),
            ];
            }
           
            else
            return ["Identity No. or Record No. is invalid"];
           
            
        default:
            return ['error' => 'Invalid endpoint'];
    }
}

// Match request URI and send response
header('Content-Type: application/json');
switch ($uri) {
    case '/mock_server.php/api/checkRequestNationalNo':
    case '/mock_server.php/api/checkRequestInfo':
    case '/mock_server.php/api/GetRequestInfo':
    case '/mock_server.php/api/Evaluation':
        echo json_encode(getMockResponse($uri, $inputData));
        break;
    default:
        http_response_code(404);
        echo json_encode(['error' => 'Not Found']);
}
