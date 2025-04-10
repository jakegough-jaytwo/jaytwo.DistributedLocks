library 'JenkinsBuilderLibrary'

helper.loadNuGetProjectDefaults('jaytwo.DistributedLocks')

def nuGetCredentialsId = 'nuget-org-jaytwo'

helper.run('linux && make && docker', {
    def timestamp = helper.getTimestamp()
    def safeJobName = helper.getSafeJobName()
    def dockerLocalTag = "jenkins__${safeJobName}__${timestamp}"
    def dockerBuilderTag = dockerLocalTag + "__builder"
    def dockerComposeProjectName = dockerLocalTag + "__testernet"
    def dockerComposeNetwork = dockerComposeProjectName + "_default"

    withEnv(["DOCKER_TAG=${dockerLocalTag}", "TIMESTAMP=${timestamp}"]) {
        try {
            stage ('Build') {
                sh "make docker-builder"
                sh "make testernet-up"
            }
            docker.image(dockerBuilderTag).inside("-e TEST_ENV=testernet --network ${dockerComposeNetwork}") {
                stage ('Unit Test') {
                    sh "make unit-test"
                }
                stage ('Pack') {
                    if(branches.isMasterBranch()){
                        sh "make pack"
                    } else {
                        sh "make pack-beta"
                    }
                }
                stage ('NuGet Check Version') {
                    sh "make nuget-check"
                }
                if (branches.isDeploymentBranch()) {
                    withCredentials([string(credentialsId: nuGetCredentialsId, variable: "NUGET_API_KEY")]) {
                        stage ('NuGet Push') {
                            sh "make nuget-push"
                        }
                    }
                }
            }
        }
        finally {
            // inside the withEnv()
            sh "make docker-copy-from-builder-output"
            sh "make testernet-clean"
            sh "make docker-clean"
        }
    }
})
