import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '10s', target: 10 }, // below normal load
    { duration: '10s', target: 300 }, // spike to 300 users
    { duration: '2m', target: 300 }, // stay at 300
    { duration: '10s', target: 10 }, // scale down
    { duration: '20s', target: 10 },
    { duration: '10s', target: 0 },
  ],
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export default function () {
  const res = http.get(`${BASE_URL}/api/students`);
  check(res, { 'status was 200': (r) => r.status == 200 });
  sleep(1);
}
